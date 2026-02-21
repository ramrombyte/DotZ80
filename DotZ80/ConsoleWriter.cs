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

/// <summary>Thin wrapper for coloured console output that respects --no-color.</summary>
public sealed class ConsoleWriter
{
    /// <summary><see langword="true"/> when ANSI colour output is enabled; <see langword="false"/> for plain text.</summary>
    private readonly bool _color;

    /// <summary>Initialises the writer with the specified colour preference.</summary>
    /// <param name="color">
    /// Pass <see langword="true"/> to enable ANSI colour output; <see langword="false"/> for plain text
    /// (e.g. when <c>--no-color</c> is set or stdout is redirected).
    /// </param>
    public ConsoleWriter(bool color) => _color = color;

    /// <summary>Writes an informational message to stdout in cyan.</summary>
    /// <param name="msg">The message to display.</param>
    public void Info   (string msg) => Write(msg, ConsoleColor.Cyan);

    /// <summary>Writes a success message to stdout in green.</summary>
    /// <param name="msg">The message to display.</param>
    public void Success(string msg) => Write(msg, ConsoleColor.Green);

    /// <summary>Writes a warning message to stdout in yellow.</summary>
    /// <param name="msg">The message to display.</param>
    public void Warning(string msg) => Write(msg, ConsoleColor.Yellow);

    /// <summary>Writes an error message to stderr in red.</summary>
    /// <param name="msg">The message to display.</param>
    public void Error  (string msg) => WriteErr(msg, ConsoleColor.Red);

    /// <summary>Writes a de-emphasised (dim) message to stdout in dark gray.</summary>
    /// <param name="msg">The message to display.</param>
    public void Dim    (string msg) => Write(msg, ConsoleColor.DarkGray);

    /// <summary>Writes a bold/highlighted message to stdout in white.</summary>
    /// <param name="msg">The message to display.</param>
    public void Bold   (string msg) => Write(msg, ConsoleColor.White);

    /// <summary>Writes a message to stdout with no colour applied.</summary>
    /// <param name="msg">The message to display.</param>
    public void Plain  (string msg) => Console.WriteLine(msg);

    /// <summary>
    /// Prints the application banner to stdout.
    /// When colour is enabled, a box-drawing border with a cyan title is rendered;
    /// otherwise, a plain single-line title with a dash separator is printed.
    /// </summary>
    public void Banner()
    {
        if (_color)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("┌─────────────────────────────────┐\n");
            Console.Write("│ ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Z80 Assembler");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(" for CP/M │");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(".NET 10");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(" │\n");
            Console.Write("└─────────────────────────────────┘\n");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Copyright (C) 2026 Menno Bolt  —  AGPL-3.0");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine("Z80 Assembler for CP/M  (.NET 10)");
            Console.WriteLine("Copyright (C) 2026 Menno Bolt  —  AGPL-3.0");
            Console.WriteLine(new string('-', 35));
        }
    }

    /// <summary>
    /// Prints a labelled section header to stdout, padded with trailing rule characters to 60 columns.
    /// A blank line is printed before the header to visually separate sections.
    /// </summary>
    /// <param name="title">The section title to display.</param>
    public void Section(string title)
    {
        Console.WriteLine();
        if (_color)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("── ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(title);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            int pad = Math.Max(0, 60 - title.Length - 3);
            Console.WriteLine(" " + new string('─', pad));
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine($"--- {title} ---");
        }
    }

    /// <summary>
    /// Prints a two-column key/value result line to stdout, left-aligning the label in 18 characters
    /// and colouring the value with the specified <see cref="ConsoleColor"/>.
    /// </summary>
    /// <param name="label">The left-hand label (e.g. <c>"Load address:"</c>).</param>
    /// <param name="value">The right-hand value string to display.</param>
    /// <param name="valColor">The colour to apply to <paramref name="value"/> when colour output is active.</param>
    public void ResultLine(string label, string value, ConsoleColor valColor = ConsoleColor.White)
    {
        if (_color)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  {label,-18}");
            Console.ForegroundColor = valColor;
            Console.WriteLine(value);
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine($"  {label,-18}{value}");
        }
    }

    /// <summary>Writes <paramref name="msg"/> to stdout, optionally applying <paramref name="color"/>.</summary>
    /// <param name="msg">The message text.</param>
    /// <param name="color">The foreground colour to use when colour output is enabled.</param>
    private void Write(string msg, ConsoleColor color)
    {
        if (_color) Console.ForegroundColor = color;
        Console.WriteLine(msg);
        if (_color) Console.ResetColor();
    }

    /// <summary>Writes <paramref name="msg"/> to stderr, optionally applying <paramref name="color"/>.</summary>
    /// <param name="msg">The message text.</param>
    /// <param name="color">The foreground colour to use when colour output is enabled.</param>
    private void WriteErr(string msg, ConsoleColor color)
    {
        if (_color) Console.ForegroundColor = color;
        Console.Error.WriteLine(msg);
        if (_color) Console.ResetColor();
    }
}
