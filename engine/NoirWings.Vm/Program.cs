using System.Diagnostics;
using System.Globalization;
using System.Text;
using IronBrew2.Obfuscator;

namespace NoirWings.Vm;

internal static class Program
{
    private const string IronBrewMitNotice = """
NoirWings generated artifact.
This artifact includes modified components of IronBrew 2.

MIT License

Copyright (c) 2019 DefCon42

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
""";

    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Length == 0)
        {
            PrintHelp();
            return 2;
        }

        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return Run(Options.Parse(args));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NoirWings VM: {exception.Message}");
            return 1;
        }
    }

    private static int Run(Options options)
    {
        var inputPath = RequireExistingFile(options.InputPath, "input");
        var outputPath = Path.GetFullPath(options.OutputPath);
        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The output path must differ from the input path.");
        }

        var toolRoot = Path.GetFullPath(options.ToolRoot ?? Path.Combine(AppContext.BaseDirectory, "runtime"));
        VerifyRuntime(toolRoot);

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDirectory))
        {
            throw new ArgumentException("The output path has no parent directory.");
        }

        Directory.CreateDirectory(outputDirectory);

        var workParent = Path.GetFullPath(options.WorkRoot ?? Path.GetTempPath());
        var jobRoot = Path.Combine(workParent, $"NoirWings-vm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(jobRoot);

        try
        {
            var runtimeDirectory = Path.Combine(jobRoot, "runtime");
            PrepareRuntime(toolRoot, jobRoot, runtimeDirectory);

            var previousDirectory = Environment.CurrentDirectory;
            var previousPath = Environment.GetEnvironmentVariable("PATH");

            try
            {
                Directory.SetCurrentDirectory(runtimeDirectory);
                Environment.SetEnvironmentVariable(
                    "PATH",
                    runtimeDirectory + Path.PathSeparator + previousPath);

                var effectiveSeed = RandomProvider.Configure(options.Seed);
                Console.WriteLine($"NoirWings VM: seed {effectiveSeed}, profile {options.Profile}.");

                var settings = BuildSettings(options);
                var tempDirectory = Path.Combine(runtimeDirectory, "temp");
                Directory.CreateDirectory(tempDirectory);
                if (!IB2.Obfuscate(tempDirectory, inputPath, settings, out var error))
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error)
                            ? "The VM backend did not produce an output file."
                            : $"The VM backend failed: {error}");
                }

                var generatedPath = Path.Combine(tempDirectory, "out.lua");
                if (!File.Exists(generatedPath))
                {
                    throw new InvalidOperationException("The VM backend completed without an output file.");
                }

                var stagedPath = Path.Combine(
                    outputDirectory,
                    $".{Path.GetFileName(outputPath)}.NoirWings-{Guid.NewGuid():N}.tmp");

                try
                {
                    var generated = File.ReadAllText(generatedPath, Encoding.Latin1);
                    var published = RenderPublishedOutput(generated);
                    File.WriteAllText(stagedPath, published, new UTF8Encoding(false));
                    ValidateLua(Path.Combine(runtimeDirectory, "luac.exe"), stagedPath, runtimeDirectory);
                    PublishStagedFile(stagedPath, outputPath);
                }
                finally
                {
                    if (File.Exists(stagedPath))
                    {
                        TryDeleteFile(stagedPath, "staged VM output");
                    }
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
                Environment.SetEnvironmentVariable("PATH", previousPath);
            }

            Console.WriteLine($"NoirWings VM: wrote {outputPath}");
            return 0;
        }
        finally
        {
            if (options.KeepWork)
            {
                Console.WriteLine($"NoirWings VM: retained work directory {jobRoot}");
            }
            else
            {
                try
                {
                    DeleteJobDirectory(jobRoot);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"NoirWings VM: work-directory cleanup warning: {exception.Message}");
                }
            }
        }
    }

    private static ObfuscationSettings BuildSettings(Options options)
    {
        var hardened = options.Profile == "hardened";
        var maximum = options.Profile == "maximum";
        return new ObfuscationSettings
        {
            // Prometheus has already encrypted source strings. This opt-in layer
            // is kept off by default because double encryption causes large
            // outputs and needs application-specific soak testing.
            EncryptStrings = options.InnerStringEncryption || maximum,
            EncryptImportantStrings = false,
            ControlFlow = true,
            BytecodeCompress = true,
            DecryptTableLen = maximum ? 750 : hardened ? 500 : 256,
            PreserveLineInfo = false,
            Mutate = true,
            SuperOperators = true,
            MaxMutations = maximum ? 320 : hardened ? 200 : 80,
            MaxMegaSuperOperators = maximum ? 180 : hardened ? 120 : 30,
            MaxMiniSuperOperators = maximum ? 180 : hardened ? 120 : 50,
            HandlerTableDispatch = true,
            MaxJunkOpcodes = maximum ? 200 : hardened ? 100 : 50,
            RealMutations = true,

            // Luraph-tier features
            OpaquePredicates = hardened || maximum,
            OpaqueDeadBlocks = maximum ? 30 : hardened ? 15 : 5,
            EnvironmentCage = hardened || maximum,
            AntiHook = hardened || maximum,
            DynamicDispatch = maximum,
            CoroutineDispatch = false,
            PhantomHandlerTables = maximum ? 5 : hardened ? 3 : 0,
            WatermarkIntegrity = true,
        };
    }

    private static void PublishStagedFile(string stagedPath, string outputPath)
    {
        if (!File.Exists(outputPath))
        {
            File.Move(stagedPath, outputPath);
            return;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath)
                              ?? throw new InvalidOperationException("The output path has no parent directory.");
        var backupPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.NoirWings-{Guid.NewGuid():N}.bak");

        var published = false;
        try
        {
            File.Replace(stagedPath, outputPath, backupPath);
            published = true;
        }
        finally
        {
            if (published && File.Exists(backupPath))
            {
                TryDeleteFile(backupPath, "published-output backup");
            }
            else if (!published && File.Exists(backupPath))
            {
                Console.Error.WriteLine(
                    $"NoirWings VM: publication failed; recovery backup preserved at {backupPath}");
            }
        }
    }

    private static void TryDeleteFile(string path, string description)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"NoirWings VM: could not remove {description} '{path}': {exception.Message}");
        }
    }

    private static string RenderPublishedOutput(string generated)
    {
        var payload = StripVendorBanner(generated);
        return "-- Protected by NoirWings | https://noirwings.dev\n" + payload;
    }

    private static string StripVendorBanner(string generated)
    {
        if (!generated.StartsWith("--[[", StringComparison.Ordinal))
        {
            return generated;
        }

        var close = generated.IndexOf("]]", StringComparison.Ordinal);
        if (close < 0)
        {
            return generated;
        }

        var banner = generated[..(close + 2)];
        if (!banner.Contains("IronBrew:tm:", StringComparison.Ordinal))
        {
            return generated;
        }

        return generated[(close + 2)..].TrimStart('\r', '\n');
    }

    private static void PrepareRuntime(string toolRoot, string jobRoot, string runtimeDirectory)
    {
        Directory.CreateDirectory(runtimeDirectory);
        CopyFile(toolRoot, runtimeDirectory, "luac.exe");
        CopyFile(toolRoot, runtimeDirectory, "luajit.exe");
        CopyFile(toolRoot, runtimeDirectory, "lua51.dll");
        CopyDirectory(
            Path.Combine(toolRoot, "Lua", "Minifier"),
            Path.Combine(jobRoot, "Lua", "Minifier"));
    }

    private static void VerifyRuntime(string toolRoot)
    {
        RequireExistingFile(Path.Combine(toolRoot, "luac.exe"), "Lua 5.1 compiler");
        RequireExistingFile(Path.Combine(toolRoot, "luajit.exe"), "LuaJIT runtime");
        RequireExistingFile(Path.Combine(toolRoot, "lua51.dll"), "Lua 5.1 library");

        var minifier = Path.Combine(toolRoot, "Lua", "Minifier", "luasrcdiet.lua");
        RequireExistingFile(minifier, "LuaSrcDiet");
    }

    private static void CopyFile(string sourceDirectory, string targetDirectory, string fileName)
    {
        File.Copy(Path.Combine(sourceDirectory, fileName), Path.Combine(targetDirectory, fileName), true);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        foreach (var sourceFile in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            var targetParent = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            File.Copy(sourceFile, targetFile, true);
        }
    }

    private static void ValidateLua(string compilerPath, string sourcePath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = compilerPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(sourcePath);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start the Lua 5.1 compiler.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may have exited between the timeout and Kill.
            }

            throw new TimeoutException("The Lua 5.1 compiler validation timed out.");
        }

        process.WaitForExit();
        var standardOutput = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The generated artifact failed Lua 5.1 validation: {standardError}{standardOutput}".Trim());
        }
    }

    private static string RequireExistingFile(string path, string displayName)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"The {displayName} file was not found.", fullPath);
        }

        return fullPath;
    }

    private static void DeleteJobDirectory(string jobRoot)
    {
        var fullPath = Path.GetFullPath(jobRoot);
        if (!Path.GetFileName(fullPath).StartsWith("NoirWings-vm-", StringComparison.Ordinal) ||
            !Directory.Exists(fullPath))
        {
            return;
        }

        Directory.Delete(fullPath, true);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
NoirWings VM backend (Lua 5.1 only)

Usage:
  dotnet NoirWings.Vm.dll --input <file.lua> --output <file.lua> [options]

Options:
  --profile <balanced|hardened|maximum>
                                      VM complexity profile (default: hardened)
  --seed <positive-integer>         Reproducible build seed
  --tool-root <directory>           Runtime assets directory
  --work-root <directory>           Parent directory for the isolated job
  --keep-work                       Keep private intermediates for diagnosis
  --inner-string-encryption         Enable the old backend's second string pass

Based on Prometheus by Elias Oelschner, https://github.com/prometheus-lua/Prometheus
""");
    }

    private sealed record Options(
        string InputPath,
        string OutputPath,
        string Profile,
        int? Seed,
        string ToolRoot,
        string WorkRoot,
        bool KeepWork,
        bool InnerStringEncryption)
    {
        public static Options Parse(string[] arguments)
        {
            string input = null;
            string output = null;
            string profile = "hardened";
            int? seed = null;
            string toolRoot = null;
            string workRoot = null;
            var keepWork = false;
            var innerStringEncryption = false;

            for (var index = 0; index < arguments.Length; index++)
            {
                var argument = arguments[index];

                string NextValue()
                {
                    index++;
                    if (index >= arguments.Length)
                    {
                        throw new ArgumentException($"Missing value after {argument}.");
                    }

                    return arguments[index];
                }

                switch (argument)
                {
                    case "--input":
                        input = NextValue();
                        break;
                    case "--output":
                        output = NextValue();
                        break;
                    case "--profile":
                        profile = NextValue().ToLowerInvariant();
                        if (profile is not ("balanced" or "hardened" or "maximum"))
                        {
                            throw new ArgumentException("Profile must be balanced, hardened, or maximum.");
                        }

                        break;
                    case "--seed":
                        if (!int.TryParse(NextValue(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSeed) ||
                            parsedSeed <= 0)
                        {
                            throw new ArgumentException("Seed must be a positive 32-bit integer.");
                        }

                        seed = parsedSeed;
                        break;
                    case "--tool-root":
                        toolRoot = NextValue();
                        break;
                    case "--work-root":
                        workRoot = NextValue();
                        break;
                    case "--keep-work":
                        keepWork = true;
                        break;
                    case "--inner-string-encryption":
                        innerStringEncryption = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {argument}");
                }
            }

            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
            {
                throw new ArgumentException("--input and --output are required.");
            }

            return new Options(
                input,
                output,
                profile,
                seed,
                toolRoot,
                workRoot,
                keepWork,
                innerStringEncryption);
        }
    }
}
