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

using System.Text;
using System.Text.RegularExpressions;

namespace DotZ80.Assembler
{
    /// <summary>
    /// A source-level preprocessor that recursively expands <c>INCLUDE "file"</c> directives
    /// before the token-based assembly passes run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each <c>INCLUDE</c> directive is replaced in-place by the full text of the referenced
    /// file so that the rest of the assembler never needs to be aware that multiple physical
    /// files were involved.
    /// </para>
    /// <para>
    /// Include file resolution order:
    /// <list type="number">
    ///   <item>The directory that contains the file making the include request.</item>
    ///   <item>Each directory supplied in <paramref name="includePaths"/> in order.</item>
    /// </list>
    /// </para>
    /// <para>Circular includes are detected via a set of canonical file paths and reported
    /// as errors rather than causing infinite recursion.</para>
    /// </remarks>
    public class Preprocessor
    {
        /// <summary>
        /// Matches an INCLUDE directive line, capturing the filename inside single or double quotes.
        /// Allows optional leading whitespace and a trailing semicolon comment.
        /// </summary>
        private static readonly Regex IncludeRegex = new Regex(
            @"^\s*INCLUDE\s+[""'](?<file>[^""']+)[""']\s*(;.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Additional directories to search when an include file is not found beside the source.</summary>
        private readonly IReadOnlyList<string> _includePaths;

        /// <summary>Accumulated preprocessing errors (e.g. file not found, circular include).</summary>
        private readonly List<string> _errors = new();

        /// <summary>
        /// Initialises a new <see cref="Preprocessor"/> with an optional list of extra search directories.
        /// </summary>
        /// <param name="includePaths">
        /// Zero or more directories to search for include files after checking the directory
        /// of the file that contains the <c>INCLUDE</c> directive.
        /// </param>
        public Preprocessor(IEnumerable<string>? includePaths = null)
        {
            _includePaths = includePaths?.ToList() ?? [];
        }

        /// <summary>All errors collected during the last call to <see cref="Process"/>.</summary>
        public IReadOnlyList<string> Errors => _errors;

        /// <summary>
        /// Processes the given source text, recursively expanding all <c>INCLUDE</c> directives,
        /// and returns the fully expanded source ready for tokenization.
        /// </summary>
        /// <param name="source">The top-level assembly source text.</param>
        /// <param name="sourceFilePath">
        /// The absolute or relative path of the file that provided <paramref name="source"/>.
        /// Used to resolve include paths relative to the source file's directory.
        /// Pass <see langword="null"/> or an empty string if the source did not originate from a file
        /// (e.g. a string literal in a test); includes will still be searched via <see cref="_includePaths"/>.
        /// </param>
        /// <returns>The expanded source text with all includes inlined.</returns>
        public string Process(string source, string? sourceFilePath)
        {
            _errors.Clear();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                try { visited.Add(Path.GetFullPath(sourceFilePath)); }
                catch { /* ignore path errors for in-memory sources */ }
            }

            return ExpandSource(source, sourceFilePath, visited, depth: 0);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private string ExpandSource(string source, string? currentFilePath, HashSet<string> visited, int depth)
        {
            const int MaxDepth = 64;
            if (depth > MaxDepth)
            {
                _errors.Add($"INCLUDE nesting depth exceeded {MaxDepth} levels — possible circular include");
                return source;
            }

            string currentDir = string.IsNullOrEmpty(currentFilePath)
                ? Directory.GetCurrentDirectory()
                : Path.GetDirectoryName(Path.GetFullPath(currentFilePath)) ?? Directory.GetCurrentDirectory();

            var lines = source.Split('\n');
            var sb    = new StringBuilder(source.Length);

            for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
            {
                string rawLine = lines[lineIdx];
                string trimmed = rawLine.TrimEnd('\r');

                var match = IncludeRegex.Match(trimmed);
                if (!match.Success)
                {
                    sb.Append(rawLine);
                    if (lineIdx < lines.Length - 1) sb.Append('\n');
                    continue;
                }

                string includeName  = match.Groups["file"].Value;
                string? resolvedPath = ResolveInclude(includeName, currentDir);

                if (resolvedPath is null)
                {
                    _errors.Add($"INCLUDE '{includeName}': file not found (searched '{currentDir}'" +
                                (_includePaths.Count > 0
                                    ? " and " + string.Join(", ", _includePaths.Select(p => $"'{p}'"))
                                    : "") + ")");
                    sb.Append($"; [INCLUDE ERROR: '{includeName}' not found]");
                    if (lineIdx < lines.Length - 1) sb.Append('\n');
                    continue;
                }

                string canonicalPath = Path.GetFullPath(resolvedPath);

                if (!visited.Add(canonicalPath))
                {
                    _errors.Add($"INCLUDE '{includeName}': circular include detected ('{canonicalPath}' already on the include stack)");
                    sb.Append($"; [INCLUDE ERROR: circular include '{includeName}']");
                    if (lineIdx < lines.Length - 1) sb.Append('\n');
                    continue;
                }

                // originalPath is the path as found on disk (before any symlink redirect).
                // After a redirect resolvedPath may point into a different directory (e.g. ../includes/),
                // but nested INCLUDEs in that file should still search the original project directory.
                // We keep originalPath so we can pass it as the context for the recursive call.
                string originalPath = resolvedPath;

                string includeSource;
                try
                {
                    // Follow .NET symlinks first; if the OS resolved it, great.
                    // On Windows, Git may check out symlinks as plain-text files
                    // whose entire content is the relative target path (e.g. "../includes/rc2014.inc").
                    // Detect that case and redirect transparently.
                    string? symlinkTarget = new FileInfo(resolvedPath).LinkTarget;
                    if (symlinkTarget is not null)
                    {
                        // Real OS symlink — .NET resolved the target for us.
                        string targetPath = Path.IsPathRooted(symlinkTarget)
                            ? symlinkTarget
                            : Path.Combine(Path.GetDirectoryName(resolvedPath)!, symlinkTarget);
                        if (File.Exists(targetPath))
                            resolvedPath = Path.GetFullPath(targetPath);
                    }
                    else
                    {
                        // Check for a Git-on-Windows fake symlink: a small text file whose
                        // sole content is a relative (or absolute) path to another file.
                        var fi = new FileInfo(resolvedPath);
                        if (fi.Length < 512)
                        {
                            string candidate = File.ReadAllText(resolvedPath).Trim();
                            if (candidate.Length > 0 &&
                                candidate.IndexOfAny(['\n', '\r', '\0']) < 0 &&
                                candidate.IndexOfAny(['/', '\\', '.']) >= 0)
                            {
                                string targetPath = Path.IsPathRooted(candidate)
                                    ? candidate
                                    : Path.Combine(Path.GetDirectoryName(resolvedPath)!, candidate);
                                if (File.Exists(targetPath))
                                    resolvedPath = Path.GetFullPath(targetPath);
                            }
                        }
                    }

                    includeSource = File.ReadAllText(resolvedPath);
                }
                catch (Exception ex)
                {
                    _errors.Add($"INCLUDE '{includeName}': cannot read '{resolvedPath}': {ex.Message}");
                    sb.Append($"; [INCLUDE ERROR: cannot read '{includeName}']");
                    if (lineIdx < lines.Length - 1) sb.Append('\n');
                    visited.Remove(canonicalPath);
                    continue;
                }

                // Pass originalPath (not resolvedPath) so that nested INCLUDEs in a symlink-redirected
                // file still search the project directory where the symlink stub lives.
                string expanded = ExpandSource(includeSource, originalPath, visited, depth + 1);
                visited.Remove(canonicalPath);

                sb.Append(expanded);
                if (expanded.Length > 0 && expanded[^1] != '\n')
                    sb.Append('\n');
            }

            return sb.ToString();
        }

        private string? ResolveInclude(string fileName, string currentDir)
        {
            string candidate = Path.Combine(currentDir, fileName);
            if (File.Exists(candidate)) return candidate;

            foreach (string dir in _includePaths)
            {
                candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }
    }
}
