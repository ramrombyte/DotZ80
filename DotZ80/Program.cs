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

using DotZ80;

// ── Parse arguments ──────────────────────────────────────────────────────────
var (opts, parseError) = CliParser.Parse(args);

var con = new ConsoleWriter(color: !opts.NoColor && !Console.IsOutputRedirected);

if (parseError is not null)
{
    con.Error($"[ERROR] {parseError}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliParser.HelpText);
    return 1;
}

if (opts.ShowVersion)
{
    Console.WriteLine(CliParser.VersionText);
    return 0;
}

if (opts.ShowHelp)
{
    Console.WriteLine(CliParser.HelpText);
    return 0;
}

// ── Banner ───────────────────────────────────────────────────────────────────
con.Banner();

var runner = new AssemblerRunner(con, opts);

// ── Batch mode ───────────────────────────────────────────────────────────────
if (opts.Batch)
{
    int totalErrors = 0;
    int total       = opts.BatchFiles.Count;
    int ok          = 0;

    con.Info($"Batch mode: {total} file(s)");
    Console.WriteLine();

    for (int i = 0; i < opts.BatchFiles.Count; i++)
    {
        string file = opts.BatchFiles[i];
        con.Section($"[{i + 1}/{total}] {Path.GetFileName(file)}");

        int code = runner.Run(file);
        if (code == 0) ok++;
        else totalErrors++;
    }

    Console.WriteLine();
    con.Section("Batch Summary");
    if (totalErrors == 0)
        con.Success($"  All {total} file(s) assembled successfully.");
    else
    {
        con.Warning($"  {ok}/{total} succeeded,  {totalErrors} failed.");
    }

    return totalErrors > 0 ? 2 : 0;
}

// ── Single file ──────────────────────────────────────────────────────────────
Console.WriteLine();
int exitCode = runner.Run(opts.InputFile!, opts.OutputFile);
Console.WriteLine();
return exitCode;
