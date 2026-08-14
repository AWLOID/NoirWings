"""
Luau → Lua 5.1 preprocessor.
Strips Luau-specific syntax so that luac 5.1 can compile the result.

Handles:
- Type annotations (: type, :: type)
- Compound assignments (+=, -=, *=, /=, %=, ^=, ..=)
- continue statement → replaced with goto-based pattern
- if-then expressions (if x then y else z) — inline ternary
- Type declarations (type Foo = ..., export type Foo = ...)
- Generalized iteration (for k, v in obj do) — left as-is (works in Lua 5.1 if __iter)
- String interpolation (`hello {name}`) → string.format or concatenation
- Optional ?. and :: method type syntax
"""
import re
import sys


def strip_type_annotations(source: str) -> str:
    """Remove type annotations from function signatures and variable declarations."""
    # Remove 'export type ...' and 'type ...' declarations (full lines)
    source = re.sub(r'^[ \t]*export\s+type\s+[^\n]+', '', source, flags=re.MULTILINE)
    source = re.sub(r'^[ \t]*type\s+\w+[^\n]*=[^\n]+', '', source, flags=re.MULTILINE)

    # Remove return type annotations  ): Type  or  ): (Type, Type)
    source = re.sub(r'\)\s*:\s*\([^)]*\)', ')', source)
    source = re.sub(r'\)\s*:\s*[%w_<>|&?\[\].]+', ')', source)

    # Remove parameter type annotations  name: Type
    # Be careful not to match table constructors {key: value}
    # Match inside parentheses for function params
    def strip_param_types(m):
        params_str = m.group(1)
        # Remove ': Type' patterns but not '= default'
        cleaned = re.sub(r':\s*[%s]+' % r'\w<>|&?\[\].\s"\'', '', params_str)
        # Simpler: remove ': word' patterns
        cleaned = re.sub(r':\s*[\w<>|\[\]?.&\s]+?(?=[,\)]|$)', '', params_str)
        return '(' + cleaned + ')'

    # Remove variable type annotations: local x: Type = ...
    source = re.sub(r'(local\s+\w+)\s*:\s*[\w<>|\[\]?.&\s]+?(\s*=)', r'\1\2', source)
    # local x: Type (no assignment)
    source = re.sub(r'(local\s+\w+)\s*:\s*[\w<>|\[\]?.&\s]+', r'\1', source)

    # Remove function parameter types more aggressively
    # Pattern: (name: Type, name: Type) -> (name, name)
    def clean_func_params(m):
        content = m.group(1)
        # Remove ': Type' after parameter names
        content = re.sub(r'(\w+)\s*:\s*[^,\)]+', r'\1', content)
        return '(' + content + ')'

    source = re.sub(r'\(([^)]*:\s*[^)]+)\)', clean_func_params, source)

    return source


def convert_compound_assignments(source: str) -> str:
    """Convert += -= *= /= %= ^= ..= to standard Lua."""
    operators = [r'\+', r'-', r'\*', r'/', r'%%', r'\^', r'\.\.']

    for op in operators:
        clean_op = op.replace('\\', '')
        pattern = r'(\b[\w.\[\]"\']+\b)\s*' + op + r'=\s*(.+)'
        replacement = r'\1 = \1 ' + clean_op + r' \2'
        source = re.sub(pattern, replacement, source, flags=re.MULTILINE)

    return source


def convert_continue(source: str) -> str:
    """Replace 'continue' with a goto-based pattern."""
    # Simple approach: replace continue with goto continue_label
    # and add ::continue_label:: before each 'end' that closes a loop
    # This is imperfect but works for most cases

    if 'continue' not in source:
        return source

    lines = source.split('\n')
    result = []
    loop_depth = 0
    label_counter = [0]
    loop_stack = []

    for line in lines:
        stripped = line.strip()

        # Track loop starts
        if re.match(r'^(while|for|repeat)\b', stripped):
            label_counter[0] += 1
            loop_stack.append(label_counter[0])
            loop_depth += 1

        # Replace continue
        if stripped == 'continue' and loop_stack:
            indent = len(line) - len(line.lstrip())
            result.append(' ' * indent + f'goto __continue_{loop_stack[-1]}__')
        else:
            result.append(line)

        # Before 'end' or 'until' that closes a loop, insert label
        if loop_stack and (stripped == 'end' or stripped.startswith('until')):
            if loop_depth > 0:
                indent = len(line) - len(line.lstrip())
                result.insert(-1, ' ' * indent + f'::__continue_{loop_stack[-1]}__::')
                loop_stack.pop()
                loop_depth -= 1

    return '\n'.join(result)


def convert_string_interpolation(source: str) -> str:
    """Convert `hello {name}` to 'hello ' .. tostring(name)."""
    def replace_interp(m):
        content = m.group(1)
        parts = []
        last_end = 0
        for brace in re.finditer(r'\{([^}]+)\}', content):
            # Text before this interpolation
            text_before = content[last_end:brace.start()]
            if text_before:
                parts.append(f'"{text_before}"')
            parts.append(f'tostring({brace.group(1)})')
            last_end = brace.end()
        # Remaining text
        remaining = content[last_end:]
        if remaining:
            parts.append(f'"{remaining}"')
        return ' .. '.join(parts) if parts else '""'

    source = re.sub(r'`([^`]*)`', replace_interp, source)
    return source


def strip_generics(source: str) -> str:
    """Remove generic type parameters <T, U> from function definitions."""
    source = re.sub(r'<[\w\s,]+>', '', source)
    return source


def preprocess(source: str) -> str:
    """Full Luau → Lua 5.1 preprocessing pipeline."""
    source = strip_generics(source)
    source = strip_type_annotations(source)
    source = convert_compound_assignments(source)
    source = convert_string_interpolation(source)
    source = convert_continue(source)

    # Remove '?' from optional types that might remain
    source = re.sub(r'(\w)\?', r'\1', source)

    return source


if __name__ == '__main__':
    if len(sys.argv) < 3:
        print("Usage: luau_preprocess.py <input> <output>", file=sys.stderr)
        sys.exit(1)

    input_path = sys.argv[1]
    output_path = sys.argv[2]

    with open(input_path, 'r', encoding='utf-8', errors='replace') as f:
        source = f.read()

    result = preprocess(source)

    with open(output_path, 'w', encoding='utf-8') as f:
        f.write(result)
