// Z80 Assembler for CP/M — Command Line Tool
// Copyright (C) 2026 Menno Bolt
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

namespace DotZ80;

/// <summary>Specifies the output file format produced by the assembler.</summary>
public enum OutputFormat
{
    /// <summary>Intel HEX text format — compatible with CP/M loaders and hardware programmers.</summary>
    Hex,

    /// <summary>Raw binary image starting at the load address.</summary>
    Binary,
}

/// <summary>Parsed command-line options.</summary>
public sealed record CliOptions
{
    /// <summary>Path to the primary input assembly source file.</summary>
    public string? InputFile    { get; init; }

    /// <summary>
    /// Explicit output file path supplied via <c>-o</c> / <c>--output</c>.
    /// When <see langword="null"/>, the output path is derived from <see cref="InputFile"/>
    /// with a <c>.hex</c> or <c>.bin</c> extension depending on <see cref="Format"/>.
    /// </summary>
    public string? OutputFile   { get; init; }

    /// <summary>
    /// Path for the assembly listing file supplied via <c>--list-file</c>.
    /// Setting this also implies <see cref="Listing"/> = <see langword="true"/>.
    /// </summary>
    public string? ListingFile  { get; init; }

    /// <summary>
    /// Path for the symbol-table file supplied via <c>--sym-file</c>.
    /// Setting this also implies <see cref="Symbols"/> = <see langword="true"/>.
    /// </summary>
    public string? SymbolFile   { get; init; }

    /// <summary>When <see langword="true"/>, prints extra diagnostic output during assembly.</summary>
    public bool    Verbose      { get; init; }

    /// <summary>When <see langword="true"/>, prints the assembly listing to the console after a successful build.</summary>
    public bool    Listing      { get; init; }

    /// <summary>When <see langword="true"/>, prints the symbol table to the console after a successful build.</summary>
    public bool    Symbols      { get; init; }

    /// <summary>When <see langword="true"/>, disables ANSI colour sequences in console output.</summary>
    public bool    NoColor      { get; init; }

    /// <summary>When <see langword="true"/>, writes the assembled output to stdout instead of saving a file.</summary>
    public bool    StdOut       { get; init; }

    /// <summary>The output format to produce. Defaults to <see cref="OutputFormat.Hex"/>.</summary>
    public OutputFormat Format  { get; init; } = OutputFormat.Hex;

    /// <summary>
    /// Optional load-address override injected as an <c>ORG</c> directive before the source is assembled.
    /// Accepts hex values such as <c>0x0100</c> or <c>0100h</c>.
    /// </summary>
    public ushort? OrgOverride  { get; init; }

    /// <summary>When <see langword="true"/>, the help text is displayed and the assembler exits.</summary>
    public bool    ShowHelp     { get; init; }

    /// <summary>When <see langword="true"/>, the version string is displayed and the assembler exits.</summary>
    public bool    ShowVersion  { get; init; }

    /// <summary>When <see langword="true"/>, multiple input files are assembled in batch mode, each producing its own output file.</summary>
    public bool    Batch        { get; init; }

    /// <summary>All input files to be assembled in batch mode (includes the primary <see cref="InputFile"/> as the first element).</summary>
    public IReadOnlyList<string> BatchFiles { get; init; } = [];
}

/// <summary>Parses command-line arguments into a <see cref="CliOptions"/> record.</summary>
public static class CliParser
{
    /// <summary>
    /// Parses the supplied command-line argument array into a <see cref="CliOptions"/> record.
    /// </summary>
    /// <param name="args">The raw argument array passed to the process entry point.</param>
    /// <returns>
    /// A tuple containing the populated <see cref="CliOptions"/> and an optional error message.
    /// When the error string is non-<see langword="null"/>, the options should be considered invalid
    /// and the error should be displayed to the user before exiting.
    /// </returns>
    public static (CliOptions opts, string? error) Parse(string[] args)
    {
        if (args.Length == 0)
            return (new CliOptions { ShowHelp = true }, null);

        string? inputFile   = null;
        string? outputFile  = null;
        string? listingFile = null;
        string? symbolFile  = null;
        bool         verbose     = false;
        bool         listing     = false;
        bool         symbols     = false;
        bool         noColor     = false;
        bool         stdOut      = false;
        bool         showHelp    = false;
        bool         showVersion = false;
        OutputFormat format      = OutputFormat.Hex;
        ushort?      orgOverride = null;
        var          batchFiles  = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg.ToLowerInvariant())
            {
                case "-h": case "--help":    showHelp    = true; break;
                case "-v": case "--version": showVersion = true; break;
                case "--verbose":            verbose     = true; break;
                case "-l": case "--listing": listing     = true; break;
                case "-s": case "--symbols": symbols     = true; break;
                case "--no-color":           noColor     = true; break;
                case "--stdout":             stdOut      = true; break;
                case "--binary":             format      = OutputFormat.Binary; break;

                case "--format":
                    if (++i >= args.Length) return (new(), $"Missing value for {arg}");
                    switch (args[i].ToLowerInvariant())
                    {
                        case "hex":              format = OutputFormat.Hex;    break;
                        case "binary": case "bin": format = OutputFormat.Binary; break;
                        default: return (new(), $"Unknown format '{args[i]}'. Valid values: hex, binary");
                    }
                    break;

                case "-o": case "--output":
                    if (++i >= args.Length) return (new(), $"Missing value for {arg}");
                    outputFile = args[i];
                    break;

                case "--list-file":
                    if (++i >= args.Length) return (new(), $"Missing value for {arg}");
                    listingFile = args[i];
                    listing = true;
                    break;

                case "--sym-file":
                    if (++i >= args.Length) return (new(), $"Missing value for {arg}");
                    symbolFile = args[i];
                    symbols = true;
                    break;

                case "--org":
                    if (++i >= args.Length) return (new(), $"Missing value for {arg}");
                    try
                    {
                        string orgStr = args[i].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? args[i][2..]
                            : args[i].TrimEnd('h', 'H');
                        orgOverride = Convert.ToUInt16(orgStr, 16);
                    }
                    catch
                    {
                        return (new(), $"Invalid --org value '{args[i]}'. Use hex e.g. 0x0100 or 0100h");
                    }
                    break;

                default:
                    if (arg.StartsWith('-'))
                        return (new(), $"Unknown option '{arg}'. Use --help for usage.");
                    // Positional — input files
                    if (inputFile is null)
                        inputFile = arg;
                    else
                        batchFiles.Add(arg);
                    break;
            }
        }

        if (!showHelp && !showVersion && inputFile is null)
            return (new(), "No input file specified. Use --help for usage.");

        // Add first input to batch list if multiple files given
        bool batch = batchFiles.Count > 0;
        if (batch) batchFiles.Insert(0, inputFile!);

        return (new CliOptions
        {
            InputFile   = inputFile,
            OutputFile  = outputFile,
            ListingFile = listingFile,
            SymbolFile  = symbolFile,
            Verbose     = verbose,
            Listing     = listing,
            Symbols     = symbols,
            NoColor     = noColor,
            StdOut      = stdOut,
            Format      = format,
            OrgOverride = orgOverride,
            ShowHelp    = showHelp,
            ShowVersion = showVersion,
            Batch       = batch,
            BatchFiles  = batchFiles,
        }, null);
    }

    /// <summary>
    /// Returns the version string read from the assembly's <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>,
    /// falling back to <see cref="System.Reflection.AssemblyName.Version"/> if the attribute is absent.
    /// </summary>
    private static string AssemblyVersion
    {
        get
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var info = (asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                           .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)
                          ?.InformationalVersion;
            // InformationalVersion may carry a git hash suffix like "1.1.0+abc1234"; strip it.
            if (!string.IsNullOrEmpty(info))
                return info.Contains('+') ? info[..info.IndexOf('+')] : info;
            return asm.GetName().Version?.ToString(3) ?? "?";
        }
    }

    /// <summary>Returns the full help text shown when the user passes <c>-h</c> or <c>--help</c>.</summary>
    public static string HelpText =>
        $"""
        dotz80 {AssemblyVersion} — Z80 Assembler for CP/M
        Copyright (C) 2026 Menno Bolt  —  AGPL-3.0
        <https://www.gnu.org/licenses/agpl-3.0.html>

        USAGE
          dotz80 <input.asm|input.z80> [options]
          dotz80 file1.asm file2.asm ...    (batch mode; .asm and .z80 accepted)

        OPTIONS
          -o, --output <file>   Output file (default: <input>.hex or <input>.bin)
              --format <fmt>    Output format: hex (default) or binary
              --binary          Shorthand for --format binary
          -l, --listing         Print assembly listing to console
              --list-file <f>   Write listing to file (implies -l)
          -s, --symbols         Print symbol table to console
              --sym-file <f>    Write symbol table to file (implies -s)
              --org <addr>      Override load address (e.g. 0100h or 0x0100)
              --stdout          Print output to stdout instead of saving a file
              --no-color        Disable ANSI colour output
              --verbose         Extra diagnostic output
          -v, --version         Show version and exit
          -h, --help            Show this help and exit

        EXAMPLES
          dotz80 hello.asm
          dotz80 hello.z80
          dotz80 hello.asm -o out/hello.hex -l -s
          dotz80 hello.asm --binary -o hello.bin
          dotz80 hello.asm --format binary --stdout | xxd
          dotz80 hello.asm --org 0x1000 --list-file hello.lst
          dotz80 a.asm b.asm c.asm          (batch: each gets its own .hex/.bin)
          dotz80 a.z80 b.z80 c.z80

        NUMBER FORMATS
          Decimal: 255    Hex: 0FFh  0xFF  $FF    Binary: 10110b

        DIRECTIVES
          ORG, EQU, DB/DEFB/DEFM, DW/DEFW, DS/DEFS, END

        OUTPUT
          hex    — Intel HEX format, compatible with CP/M loaders,
                   hardware programmers, and emulators (e.g. RunCPM, MAME).
          binary — Raw binary image starting at load address.
        """;

    /// <summary>Returns the version string shown when the user passes <c>-v</c> or <c>--version</c>.</summary>
    public static string VersionText =>
        $" " + Environment.NewLine +
        $"dotz80 {AssemblyVersion} for (.NET 10 / C# 13)  | Z80 Assembler for CP/M" + Environment.NewLine +
        $"Copyright (C) 2026 Menno Bolt  —  AGPL-3.0";
}
