"""
Luau → Lua 5.1 preprocessor (token-aware).

Converts Luau/Roblox scripts to valid Lua 5.1 by:
- Removing type annotations
- Converting compound assignments (+=, -=, etc.)
- Replacing `continue` with goto pattern (Lua 5.2+ goto, supported by luajit)
- Converting string interpolation
- Removing type declarations
- Removing `::` type cast syntax
"""
import re
import sys


def preprocess(source: str) -> str:
    """Full Luau → Lua 5.1 preprocessing."""
    source = _remove_type_declarations(source)
    source = _remove_type_annotations_safe(source)
    source = _convert_compound_assignments(source)
    source = _convert_string_interpolation(source)
    source = _convert_continue(source)
    source = _remove_type_casts(source)
    source = _cleanup(source)
    return source


def _remove_type_declarations(source: str) -> str:
    """Remove standalone type declarations."""
    # export type Name = ...
    # type Name = ...
    # These can span multiple lines if they have { } blocks
    lines = source.split('\n')
    result = []
    skip_until_balanced = False
    brace_depth = 0

    for line in lines:
        stripped = line.strip()

        if skip_until_balanced:
            brace_depth += line.count('{') - line.count('}')
            if brace_depth <= 0:
                skip_until_balanced = False
            continue

        if re.match(r'^(export\s+)?type\s+\w+', stripped):
            # Check if it has opening brace — multi-line type
            brace_depth = line.count('{') - line.count('}')
            if brace_depth > 0:
                skip_until_balanced = True
            continue

        result.append(line)

    return '\n'.join(result)


def _remove_type_annotations_safe(source: str) -> str:
    """Remove type annotations without breaking code."""
    lines = source.split('\n')
    result = []

    for line in lines:
        line = _process_line_types(line)
        result.append(line)

    return '\n'.join(result)


def _process_line_types(line: str) -> str:
    """Process a single line to remove type annotations."""
    # Skip strings — find code portions only
    # Simple approach: process outside of string literals

    # Remove return type from function declarations: function(...): ReturnType
    # Pattern: ): TypeStuff  at end or before newline
    line = re.sub(r'\)\s*:\s*\([\w\s,|?<>\[\]{}_.]+\)', ')', line)
    line = re.sub(r'\)\s*:\s*[\w|?<>\[\]{}_.]+(?=\s*$|\s*--)', ')', line)

    # Remove parameter type annotations inside function params
    # foo(x: number, y: string) → foo(x, y)
    # But don't touch table constructors like {key = value} or dict["key"]
    # Only match inside balanced parentheses that look like function params
    line = _strip_param_annotations(line)

    # Remove local variable type annotations
    # local x: Type = value → local x = value
    line = re.sub(r'(local\s+[\w,\s]+)\s*:\s*[\w|?<>\[\]{}_.&\s]+?(\s*=)', r'\1\2', line)
    # local x: Type  (no assignment, end of line)
    line = re.sub(r'(local\s+\w+)\s*:\s*[\w|?<>\[\]{}_.&]+\s*$', r'\1', line)
    # local x: Type  (no assignment, before comment)
    line = re.sub(r'(local\s+\w+)\s*:\s*[\w|?<>\[\]{}_.&]+(\s*--)', r'\1\2', line)

    return line


def _strip_param_annotations(line: str) -> str:
    """Strip type annotations from function parameters."""
    # Find function-like patterns: function name(params) or (params) =>
    # Match balanced parens that contain ':'

    def replace_params(m):
        full = m.group(0)
        prefix = m.group(1)
        inner = m.group(2)

        # Don't touch if this looks like a table constructor or ternary
        if prefix and prefix.strip().endswith('{'):
            return full

        # Only process if there's a colon that looks like type annotation
        if ':' not in inner:
            return full

        # Split by comma, remove type annotations from each param
        params = []
        depth = 0
        current = ''
        for ch in inner:
            if ch in '({[':
                depth += 1
                current += ch
            elif ch in ')}]':
                depth -= 1
                current += ch
            elif ch == ',' and depth == 0:
                params.append(current.strip())
                current = ''
            else:
                current += ch
        if current.strip():
            params.append(current.strip())

        cleaned_params = []
        for p in params:
            # Remove ': Type' but keep '...' and default values
            # param: Type = default → param = default
            # param: Type → param
            p = re.sub(r'^(\.\.\.)\s*:\s*[\w|?<>\[\]{}_.&]+', r'\1', p)
            p = re.sub(r'^(\w+)\s*:\s*[\w|?<>\[\]{}_.&\s]+?(\s*=.+)$', r'\1\2', p)
            p = re.sub(r'^(\w+)\s*:\s*[\w|?<>\[\]{}_.&]+', r'\1', p)
            cleaned_params.append(p)

        return prefix + '(' + ', '.join(cleaned_params) + ')'

    # Match (params_with_colon)
    line = re.sub(r'([\w.]*\s*)\(([^)]*:[^)]*)\)', replace_params, line)
    return line


def _convert_compound_assignments(source: str) -> str:
    """Convert += -= *= /= %= ^= ..= to standard Lua 5.1."""
    lines = source.split('\n')
    result = []

    for line in lines:
        # Skip lines inside strings (very basic check)
        stripped = line.strip()
        if stripped.startswith('--'):
            result.append(line)
            continue

        # Match: identifier += expression
        # Be careful with == !== ~= (comparison operators)
        for op_pat, op_char in [
            (r'\.\.=', '..'),
            (r'\+=', '+'),
            (r'-=', '-'),
            (r'\*=', '*'),
            (r'/=', '/'),
            (r'%%=', '%'),
            (r'\^=', '^'),
        ]:
            pattern = r'^(\s*)([\w.\[\]"\'()]+)\s*' + op_pat + r'\s*(.+)$'
            m = re.match(pattern, line)
            if m:
                indent = m.group(1)
                var = m.group(2)
                expr = m.group(3)
                line = f'{indent}{var} = {var} {op_char} ({expr})'
                break

        result.append(line)

    return '\n'.join(result)


def _convert_string_interpolation(source: str) -> str:
    """Convert `text {expr} text` to "text" .. tostring(expr) .. "text"."""
    def replace_interp(m):
        content = m.group(1)
        if '{' not in content:
            # No interpolation, just convert to regular string
            return '"' + content.replace('"', '\\"') + '"'
        parts = []
        last_end = 0
        for brace in re.finditer(r'\{([^}]+)\}', content):
            text_before = content[last_end:brace.start()]
            if text_before:
                parts.append('"' + text_before.replace('"', '\\"') + '"')
            parts.append('tostring(' + brace.group(1) + ')')
            last_end = brace.end()
        remaining = content[last_end:]
        if remaining:
            parts.append('"' + remaining.replace('"', '\\"') + '"')
        return ' .. '.join(parts) if parts else '""'

    source = re.sub(r'`([^`]*)`', replace_interp, source)
    return source


def _convert_continue(source: str) -> str:
    """Replace 'continue' with goto-based equivalent."""
    if '\ncontinue' not in '\n' + source and '\tcontinue' not in source and '  continue' not in source:
        if not re.search(r'^\s*continue\s*$', source, re.MULTILINE):
            return source

    lines = source.split('\n')
    result = []
    loop_stack = []  # stack of label ids
    label_id = 0
    # Track which ends correspond to loops
    block_stack = []  # 'loop' or 'other'

    for i, line in enumerate(lines):
        stripped = line.strip()
        indent = len(line) - len(line.lstrip())

        # Detect loop starts
        if re.match(r'^\s*(for|while|repeat)\b', line):
            label_id += 1
            loop_stack.append(label_id)
            block_stack.append('loop')
            result.append(line)
        elif re.match(r'^\s*(if|do|function|local\s+function)\b', line) and not re.match(r'^\s*(for|while)\b', line):
            block_stack.append('other')
            result.append(line)
        elif stripped == 'continue':
            if loop_stack:
                result.append(' ' * indent + f'goto __continue_{loop_stack[-1]}__')
            else:
                result.append(line)  # leave as is, will error but at least won't crash preprocessor
        elif stripped == 'end' or stripped.startswith('until'):
            if block_stack and block_stack[-1] == 'loop':
                # Insert continue label before end/until
                result.append(' ' * indent + f'::__continue_{loop_stack[-1]}__::')
                loop_stack.pop()
                block_stack.pop()
            elif block_stack:
                block_stack.pop()
            result.append(line)
        else:
            result.append(line)

    return '\n'.join(result)


def _remove_type_casts(source: str) -> str:
    """Remove :: type cast syntax (expr :: Type → expr)."""
    source = re.sub(r'\s*::\s*[\w|?<>\[\]{}_.&]+', '', source)
    return source


def _cleanup(source: str) -> str:
    """Final cleanup pass."""
    # Remove leftover '?' from optional types on identifiers (but not in strings)
    # Be conservative — only remove ? right after a word char at specific positions
    # Actually skip this — too risky to break ternary-like patterns

    # Remove empty lines that were left by type removal (collapse multiple blank lines)
    source = re.sub(r'\n{3,}', '\n\n', source)

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
