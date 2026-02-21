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

using DotZ80.Assembler;

namespace DotZ80;

/// <summary>
/// Orchestrates a single assembly run: reads the source file, invokes
/// <see cref="Z80AssemblerEngine"/>, and writes the requested output files
/// (machine code, listing, symbol table) while reporting progress via
/// <see cref="ConsoleWriter"/>.
/// </summary>
public sealed class AssemblerRunner(ConsoleWriter con, CliOptions opts)
{
    /// <summary>The underlying two-pass assembler engine used to translate source text to machine code.</summary>
    private readonly Z80AssemblerEngine _engine = new();

    /// <summary>Assemble a single file. Returns exit code (0 = success).</summary>
    /// <param name="inputFile">Path to the Z80 assembly source file to assemble.</param>
    /// <param name="outputFile">
    /// Optional explicit output path. When <see langword="null"/>, the path is derived from
    /// <paramref name="inputFile"/> with a <c>.hex</c> or <c>.bin</c> extension based on
    /// <see cref="CliOptions.Format"/>.
    /// </param>
    /// <returns>
    /// An exit code: <c>0</c> on success, <c>1</c> if the source file could not be read,
    /// <c>2</c> if the assembly failed with errors, or <c>3</c> if the output file could not be written.
    /// </returns>
    public int Run(string inputFile, string? outputFile = null)
    {
        // ── Read source ──────────────────────────────────────────────────────
        string source;
        try
        {
            source = File.ReadAllText(inputFile);
        }
        catch (Exception ex)
        {
            con.Error($"[ERROR] Cannot read '{inputFile}': {ex.Message}");
            return 1;
        }

        string inputName = Path.GetFileName(inputFile);

        if (opts.Verbose)
            con.Dim($"  Reading: {inputFile}  ({source.Length:N0} chars)");

        // ── Inject ORG override ──────────────────────────────────────────────
        if (opts.OrgOverride.HasValue)
        {
            source = $"        ORG     {opts.OrgOverride.Value:X4}h\n{source}";
            if (opts.Verbose)
                con.Dim($"  ORG override: 0x{opts.OrgOverride.Value:X4}");
        }

        // ── Assemble ─────────────────────────────────────────────────────────
        var sw = System.Diagnostics.Stopwatch.StartNew();
        AssemblyResult result = _engine.Assemble(source);
        sw.Stop();

        // ── Print errors/warnings ────────────────────────────────────────────
        foreach (var err in result.Errors)
            con.Error($"  [ERROR] {err}");

        foreach (var warn in result.Warnings)
            con.Warning($"  [WARN]  {warn}");

        if (!result.Success)
        {
            con.Error($"  FAILED — {result.Errors.Count} error(s) in '{inputName}'");
            return 2;
        }

        // ── Summary ──────────────────────────────────────────────────────────
        con.Success($"  OK  {inputName}");
        con.ResultLine("Load address:", $"0x{result.LoadAddress:X4}", ConsoleColor.Cyan);
        con.ResultLine("Code size:",    $"{result.Binary.Length} bytes", ConsoleColor.Cyan);
        con.ResultLine("Labels:",       $"{result.Symbols.Count}", ConsoleColor.DarkCyan);
        con.ResultLine("Assembled in:", $"{sw.ElapsedMilliseconds} ms", ConsoleColor.DarkGray);

        if (result.Warnings.Count > 0)
            con.ResultLine("Warnings:", $"{result.Warnings.Count}", ConsoleColor.Yellow);

        // ── Write output file ────────────────────────────────────────────────
        bool isBinary = opts.Format == OutputFormat.Binary;
        if (opts.StdOut)
        {
            if (isBinary)
            {
                using var stdout = Console.OpenStandardOutput();
                stdout.Write(result.Binary, 0, result.Binary.Length);
            }
            else
            {
                Console.Write(result.IntelHex);
            }
        }
        else
        {
            string defaultExt = isBinary ? ".bin" : ".hex";
            outputFile ??= Path.ChangeExtension(inputFile, defaultExt);
            try
            {
                string? dir = Path.GetDirectoryName(outputFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (isBinary)
                {
                    File.WriteAllBytes(outputFile, result.Binary);
                    con.ResultLine("BIN output:", outputFile, ConsoleColor.Green);
                }
                else
                {
                    File.WriteAllText(outputFile, result.IntelHex);
                    con.ResultLine("HEX output:", outputFile, ConsoleColor.Green);
                }
            }
            catch (Exception ex)
            {
                con.Error($"  [ERROR] Cannot write '{outputFile}': {ex.Message}");
                return 3;
            }
        }

        // ── Listing ──────────────────────────────────────────────────────────
        if (opts.Listing || opts.ListingFile is not null)
        {
            string listingText = BuildListing(result);

            if (opts.Listing)
            {
                con.Section("Assembly Listing");
                con.Dim(listingText);
            }

            if (opts.ListingFile is not null)
            {
                string lf = opts.Batch
                    ? Path.ChangeExtension(inputFile, ".lst")
                    : opts.ListingFile;
                try
                {
                    File.WriteAllText(lf, listingText);
                    con.ResultLine("Listing file:", lf, ConsoleColor.DarkCyan);
                }
                catch (Exception ex)
                {
                    con.Warning($"  [WARN] Cannot write listing '{lf}': {ex.Message}");
                }
            }
        }

        // ── Symbol table ─────────────────────────────────────────────────────
        if (opts.Symbols || opts.SymbolFile is not null)
        {
            string symText = BuildSymbolTable(result);

            if (opts.Symbols)
            {
                con.Section("Symbol Table");
                con.Dim(symText);
            }

            if (opts.SymbolFile is not null)
            {
                string sf = opts.Batch
                    ? Path.ChangeExtension(inputFile, ".sym")
                    : opts.SymbolFile;
                try
                {
                    File.WriteAllText(sf, symText);
                    con.ResultLine("Symbol file:", sf, ConsoleColor.DarkCyan);
                }
                catch (Exception ex)
                {
                    con.Warning($"  [WARN] Cannot write symbols '{sf}': {ex.Message}");
                }
            }
        }

        return 0;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a human-readable assembly listing from the given <see cref="AssemblyResult"/>,
    /// including a dated header, column labels, and one line per assembled instruction.
    /// </summary>
    /// <param name="result">The assembly result whose <see cref="AssemblyResult.Listing"/> lines are formatted.</param>
    /// <returns>A multi-line string suitable for console display or writing to a <c>.lst</c> file.</returns>
    private static string BuildListing(AssemblyResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"; Z80 Assembly Listing — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"; Load address: 0x{result.LoadAddress:X4}  Code size: {result.Binary.Length} bytes");
        sb.AppendLine(new string(';', 70));
        sb.AppendLine();
        sb.AppendLine($"{"Address",-8} {"Bytes",-14} Source");
        sb.AppendLine(new string('-', 60));
        foreach (string line in result.Listing)
            sb.AppendLine(line);
        return sb.ToString();
    }

    /// <summary>
    /// Builds a human-readable symbol table from the given <see cref="AssemblyResult"/>,
    /// listing every label with its hex and decimal address values, sorted alphabetically.
    /// </summary>
    /// <param name="result">The assembly result whose <see cref="AssemblyResult.Symbols"/> dictionary is formatted.</param>
    /// <returns>A multi-line string suitable for console display or writing to a <c>.sym</c> file.</returns>
    private static string BuildSymbolTable(AssemblyResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"; Symbol Table — {result.Symbols.Count} label(s)");
        sb.AppendLine(new string(';', 50));
        sb.AppendLine($"{"Label",-24} {"Hex",-8} {"Dec",-8}");
        sb.AppendLine(new string('-', 44));

        foreach (var kv in result.Symbols.OrderBy(x => x.Key))
            sb.AppendLine($"{kv.Key,-24} {kv.Value:X4}     {kv.Value,-8}");

        return sb.ToString();
    }
}
