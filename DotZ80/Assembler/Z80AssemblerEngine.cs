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

namespace DotZ80.Assembler
{
    /// <summary>Represents a single diagnostic message (error or warning) produced during assembly.</summary>
    public class AssemblerError
    {
        /// <summary>The 1-based source line number where the error was detected.</summary>
        public int Line { get; set; }

        /// <summary>A human-readable description of the problem.</summary>
        public string Message { get; set; }

        /// <summary>
        /// <see langword="true"/> if this is a non-fatal warning; <see langword="false"/> for a hard error
        /// that prevents a successful assembly result.
        /// </summary>
        public bool IsWarning { get; set; }

        /// <summary>Initialises a new diagnostic message.</summary>
        /// <param name="line">The 1-based source line where the issue was detected.</param>
        /// <param name="message">A description of the problem.</param>
        /// <param name="isWarning">
        /// <see langword="true"/> to classify this as a warning; <see langword="false"/> (default) for an error.
        /// </param>
        public AssemblerError(int line, string message, bool isWarning = false)
        {
            Line = line;
            Message = message;
            IsWarning = isWarning;
        }

        /// <summary>Returns a formatted diagnostic string, e.g. <c>Line 12: ERROR: Unknown mnemonic 'FOO'</c>.</summary>
        public override string ToString() =>
            $"Line {Line}: {(IsWarning ? "WARNING" : "ERROR")}: {Message}";
    }

    /// <summary>
    /// Holds the full output produced by a single invocation of <see cref="Z80AssemblerEngine.Assemble"/>.
    /// </summary>
    public class AssemblyResult
    {
        /// <summary>The raw machine-code bytes starting at <see cref="LoadAddress"/>.</summary>
        public byte[] Binary { get; set; }

        /// <summary>
        /// The assembled program in Intel HEX text format.
        /// This property is only populated when <see cref="Success"/> is <see langword="true"/>.
        /// </summary>
        public string IntelHex { get; set; }

        /// <summary>All hard errors encountered during assembly (warnings are excluded).</summary>
        public List<AssemblerError> Errors { get; set; } = new List<AssemblerError>();

        /// <summary>All warnings encountered during assembly (errors are excluded).</summary>
        public List<AssemblerError> Warnings { get; set; } = new List<AssemblerError>();

        /// <summary>The label symbol table mapping each label name to its 16-bit address.</summary>
        public Dictionary<string, ushort> Symbols { get; set; } = new Dictionary<string, ushort>();

        /// <summary>Per-instruction listing lines in the format <c>ADDR  HEX_BYTES  SOURCE</c>.</summary>
        public List<string> Listing { get; set; } = new List<string>();

        /// <summary><see langword="true"/> when assembly produced no hard errors.</summary>
        public bool Success => !Errors.Any();

        /// <summary>
        /// The base load address of the program (defaults to <c>0x0100</c>, the CP/M TPA entry point).
        /// Set by the first <c>ORG</c> directive encountered in the source.
        /// </summary>
        public ushort LoadAddress { get; set; } = 0x0100; // CP/M default
    }

    /// <summary>
    /// Two-pass Z80 assembler engine.  Converts Z80 assembly source text to machine code,
    /// Intel HEX, an assembly listing, and a symbol table.
    /// </summary>
    /// <remarks>
    /// <para><b>Pass 1</b> tokenizes the source, collects all label definitions with their
    /// addresses, and estimates instruction sizes so that forward-reference labels resolve
    /// correctly in Pass 2.</para>
    /// <para><b>Pass 2</b> regenerates the token stream and encodes each instruction to bytes.
    /// Forward references (labels used before their definition) are recorded as patches and
    /// resolved after Pass 2 via <see cref="ApplyPatches"/>.</para>
    /// </remarks>
    public class Z80AssemblerEngine
    {
        /// <summary>The label symbol table, populated during Pass 1 and refined in Pass 2.</summary>
        private Dictionary<string, ushort> _symbols = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Accumulated errors and warnings from the current assembly run.</summary>
        private List<AssemblerError> _errors = new List<AssemblerError>();

        /// <summary>Per-instruction listing lines built during Pass 2.</summary>
        private List<string> _listing = new List<string>();

        /// <summary>The current program counter, updated as instructions are assembled.</summary>
        private ushort _pc = 0x0100;

        /// <summary>The raw machine-code bytes emitted by Pass 2.</summary>
        private List<byte> _output = new List<byte>();

        /// <summary>
        /// The base load address of the program — set by the first <c>ORG</c> directive
        /// and used as the starting address for both the symbol table and Intel HEX output.
        /// </summary>
        private ushort _loadAddress = 0x0100;

        /// <summary>
        /// Forward-reference patch records: each entry stores the byte offset within
        /// <see cref="_output"/>, the referenced label name, the source line, and whether
        /// the reference is relative (JR/DJNZ) or absolute (JP/CALL).
        /// </summary>
        private List<(int offset, string label, int line, bool isRelative)> _patches =
            new List<(int, string, int, bool)>();

        /// <summary>
        /// Assembles the given Z80 assembly source text and returns a fully populated
        /// <see cref="AssemblyResult"/>.
        /// </summary>
        /// <remarks>
        /// The method resets all internal state before each invocation so that a single
        /// <see cref="Z80AssemblerEngine"/> instance may be reused for multiple files.
        /// </remarks>
        /// <param name="source">The complete assembly source text to assemble.</param>
        /// <returns>An <see cref="AssemblyResult"/> containing the machine code, Intel HEX,
        /// listing, symbol table, and any errors or warnings.</returns>
        public AssemblyResult Assemble(string source)
        {
            _symbols.Clear();
            _errors.Clear();
            _listing.Clear();
            _output.Clear();
            _patches.Clear();
            _pc = 0x0100;
            _loadAddress = 0x0100;

            var lexer = new Lexer();
            var tokens = lexer.Tokenize(source);

            // Pass 1: collect labels, calculate addresses
            Pass1(tokens, source);

            // Pass 2: generate code
            _output.Clear();
            _pc = _loadAddress;
            Pass2(tokens, source);

            // Apply patches
            ApplyPatches();

            var result = new AssemblyResult
            {
                Binary = _output.ToArray(),
                Errors = _errors.Where(e => !e.IsWarning).ToList(),
                Warnings = _errors.Where(e => e.IsWarning).ToList(),
                Symbols = new Dictionary<string, ushort>(_symbols),
                Listing = new List<string>(_listing),
                LoadAddress = _loadAddress
            };

            if (result.Success)
                result.IntelHex = GenerateIntelHex(_output.ToArray(), _loadAddress);

            return result;
        }

        /// <summary>
        /// Pass 1: scans the token stream to collect label definitions and estimate
        /// instruction sizes so that the program counter advances correctly for all
        /// subsequent labels, including forward references.
        /// </summary>
        /// <param name="tokens">The flat token list produced by the <see cref="Lexer"/>.</param>
        /// <param name="source">The original source text (used for error context only).</param>
        private void Pass1(List<Token> tokens, string source)
        {
            ushort pc = 0x0100;
            int i = 0;
            bool atLineStart = true; // true after a NewLine (or at start of file)

            while (i < tokens.Count && tokens[i].Type != TokenType.EOF)
            {
                var tok = tokens[i];

                if (tok.Type == TokenType.NewLine) { i++; atLineStart = true; continue; }

                // Label detection: IDENT followed by COLON
                if (tok.Type == TokenType.Identifier && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Colon)
                {
                    if (_symbols.ContainsKey(tok.Value))
                        _errors.Add(new AssemblerError(tok.Line, $"Duplicate label '{tok.Value}'"));
                    else
                        _symbols[tok.Value] = pc;
                    i += 2; // skip label and colon
                    atLineStart = false;
                    continue;
                }

                // EQU / SET without colon: IDENT EQU expr  or  IDENT SET expr
                if (tok.Type == TokenType.Identifier && i + 1 < tokens.Count &&
                    tokens[i + 1].Type == TokenType.Mnemonic &&
                    (tokens[i + 1].Value.Equals("EQU", StringComparison.OrdinalIgnoreCase) ||
                     tokens[i + 1].Value.Equals("SET", StringComparison.OrdinalIgnoreCase)))
                {
                    // Symbol value resolved in Pass 2; just skip line here
                    while (i < tokens.Count && tokens[i].Type != TokenType.NewLine) i++;
                    atLineStart = false;
                    continue;
                }

                // 8080-style colonless label: identifier at start of line followed by mnemonic or NewLine
                // (8080 convention: label at column 0 without a trailing colon)
                if (atLineStart && tok.Type == TokenType.Identifier && i + 1 < tokens.Count &&
                    (tokens[i + 1].Type == TokenType.NewLine ||
                     tokens[i + 1].Type == TokenType.Mnemonic ||
                     tokens[i + 1].Type == TokenType.EOF))
                {
                    if (!_symbols.ContainsKey(tok.Value))
                        _symbols[tok.Value] = pc;
                    i++; // skip just the label, keep the mnemonic for encoding
                    atLineStart = false;
                    continue;
                }

                if (tok.Type == TokenType.Mnemonic)
                {
                    string mn = tok.Value.ToUpper();

                    if (mn == "ORG")
                    {
                        i++;
                        if (i < tokens.Count)
                        {
                            pc = (ushort)EvaluateExpr(tokens, ref i, pc, 0);
                            // Update _loadAddress on the first ORG before any code is emitted
                            if (_output.Count == 0)
                                _loadAddress = pc;
                            continue;
                        }
                    }
                    else if (mn == "EQU" || mn == "SET")
                    {
                        // handled in pass2 context; skip line
                        while (i < tokens.Count && tokens[i].Type != TokenType.NewLine) i++;
                        continue;
                    }
                    else if (mn == "DEFC")
                    {
                        // DEFC SYMBOL = expr  — Zilog/Z88DK define-constant syntax
                        i++; // skip DEFC
                        if (i < tokens.Count && tokens[i].Type == TokenType.Identifier)
                        {
                            string symName = tokens[i].Value;
                            i++; // skip symbol name
                            if (i < tokens.Count && tokens[i].Type == TokenType.Equals) i++; // skip '='
                            long val = EvaluateExpr(tokens, ref i, pc, 0);
                            _symbols[symName] = (ushort)val;
                        }
                        while (i < tokens.Count && tokens[i].Type != TokenType.NewLine) i++;
                        continue;
                    }
                    else if (mn == "PUBLIC" || mn == "EXTERN" || mn == "GLOBAL"
                          || mn == "MODULE" || mn == "SECTION")
                    {
                        // Linkage/section directives — silently ignore in Pass 1
                        while (i < tokens.Count && tokens[i].Type != TokenType.NewLine) i++;
                        continue;
                    }
                    else if (mn == "IF" || mn == "ELSE" || mn == "ENDIF" ||
                             mn == "TITLE" || mn == "PAGE" || mn == "EJECT" ||
                             mn == "NAME" || mn == "MACLIB")
                    {
                        // Unsupported meta-directives — skip silently in Pass 1
                        while (i < tokens.Count && tokens[i].Type != TokenType.NewLine) i++;
                        continue;
                    }
                    else if (mn == "END")
                    {
                        break;
                    }
                    else
                    {
                        // Estimate instruction size
                        int size = EstimateSize(tokens, i, pc);
                        pc += (ushort)size;
                        // Skip to next newline
                        while (i < tokens.Count && tokens[i].Type != TokenType.NewLine) i++;
                    }
                }
                else if (tok.Type == TokenType.Identifier)
                {
                    // An identifier here is not a label (no colon follows) and not an EQU definition.
                    // Skip the line — Pass 2 will report the error once.
                    while (i < tokens.Count && tokens[i].Type != TokenType.NewLine) i++;
                }

                atLineStart = false;
                i++;
            }
        }

        /// <summary>
        /// Estimates the byte size of the instruction starting at <paramref name="start"/>
        /// within the token list without emitting any bytes.  Used by Pass 1 to advance
        /// the program counter for label resolution.
        /// </summary>
        /// <param name="tokens">The full token list.</param>
        /// <param name="start">Index of the mnemonic token for the instruction to estimate.</param>
        /// <param name="pc">The current program counter value (used for DS size expressions).</param>
        /// <returns>The estimated number of bytes the instruction will occupy.</returns>
        private int EstimateSize(List<Token> tokens, int start, ushort pc)
        {
            // Collect line tokens
            var line = new List<Token>();
            int i = start;
            while (i < tokens.Count && tokens[i].Type != TokenType.NewLine && tokens[i].Type != TokenType.EOF)
                line.Add(tokens[i++]);

            if (line.Count == 0) return 0;
            string mn = line[0].Value.ToUpper();

            switch (mn)
            {
                case "NOP": case "HALT": case "DI": case "EI": case "EXX":
                case "RLCA": case "RRCA": case "RLA": case "RRA":
                case "DAA": case "CPL": case "SCF": case "CCF": case "NEG":
                case "RETI": case "RETN": return 1;

                case "RET": return line.Count > 1 ? 1 : 1;
                case "EX": return HasIXIY(line) ? 2 : 1;

                case "INC": case "DEC":
                    if (line.Count > 1 && IsIndirectHL(line, 1)) return 1;
                    if (HasIXIY(line)) return 3;
                    return 1;

                case "ADD": case "ADC": case "SBC":
                    if (HasIXIY(line)) return 2;
                    if (IsImmediate(line, line.Count > 2 ? 2 : 1)) return 2;
                    return 1;

                case "SUB": case "AND": case "OR": case "XOR": case "CP":
                    if (HasIXIY(line)) return 3;
                    if (line.Count > 1 && IsImmediate(line, 1)) return 2;
                    return 1;

                case "LD":
                    return EstimateLDSize(line);

                case "JP":
                    if (line.Count > 1 && IsRegOrIndirect(line)) return 1;
                    return 3;

                case "JR": case "DJNZ": return 2;
                case "CALL": return 3;

                case "PUSH": case "POP":
                    return HasIXIY(line) ? 2 : 1;

                case "RST": return 1;

                case "IN": case "OUT":
                    if (HasIXIY(line)) return 3;
                    return 2;

                case "BIT": case "SET": case "RES":
                    if (HasIXIY(line)) return 4;
                    return 2;

                case "RL": case "RR": case "RLC": case "RRC":
                case "SLA": case "SRA": case "SRL": case "SLL":
                    if (HasIXIY(line)) return 4;
                    return 2;

                case "IM": return 2;

                case "INI": case "IND": case "INIR": case "INDR":
                case "OUTI": case "OUTD": case "OTIR": case "OTDR":
                case "LDI": case "LDD": case "LDIR": case "LDDR":
                case "CPI": case "CPD": case "CPIR": case "CPDR":
                    return 2;

                case "DB": case "DEFB": case "DEFM": return CountDBSize(line);
                case "DW": case "DEFW": return CountDWSize(line);
                case "DS": case "DEFS":
                {
                    if (line.Count <= 1) return 0;
                    int idx = 1;
                    try { return (int)EvaluateExpr(line, ref idx, pc, 0); }
                    catch { return 1; }
                }

                // ── Intel 8080 size estimates ─────────────────────────────────────
                case "MOV": case "ANA": case "ORA": case "XRA": case "CMP":
                case "SBB":
                case "INR": case "DCR":
                case "RAL": case "RAR":
                case "XCHG": case "XTHL": case "SPHL": case "PCHL":
                case "CMA": case "STC": case "CMC": case "HLT":
                case "RNZ": case "RZ": case "RNC": case "RC":
                case "RPO": case "RPE": case "RP": case "RM":
                    return 1;

                case "MVI": case "ADI": case "ACI": case "SUI": case "SBI":
                case "ANI": case "ORI": case "XRI":
                case "INX": case "DCX": case "DAD":
                case "LDAX": case "STAX":
                    return 2;

                case "LXI": case "LDA": case "STA": case "LHLD": case "SHLD":
                case "JMP": case "JNZ": case "JZ": case "JNC": case "JC":
                case "JPO": case "JPE": case "JM":
                case "CNZ": case "CZ": case "CNC": case "CC":
                case "CPO": case "CPE": case "CM":
                    return 3;

                // Meta-directives — no bytes
                case "IF": case "ELSE": case "ENDIF":
                case "TITLE": case "PAGE": case "EJECT":
                case "NAME": case "MACLIB": case "STKLN":
                    return 0;

                default: return 1;
            }
        }

        /// <summary>
        /// Returns <see langword="true"/> if any token on <paramref name="line"/> names an
        /// IX or IY index register (including the half-registers IXH, IXL, IYH, IYL).
        /// </summary>
        /// <param name="line">The tokens of a single instruction line.</param>
        private bool HasIXIY(List<Token> line)
        {
            return line.Any(t => t.Value.ToUpper() == "IX" || t.Value.ToUpper() == "IY" ||
                                 t.Value.ToUpper() == "IXH" || t.Value.ToUpper() == "IXL" ||
                                 t.Value.ToUpper() == "IYH" || t.Value.ToUpper() == "IYL");
        }

        /// <summary>
        /// Returns <see langword="true"/> if the tokens on <paramref name="line"/> contain
        /// an <c>(HL)</c> indirect operand starting at position <paramref name="from"/>.
        /// </summary>
        /// <param name="line">The tokens of a single instruction line.</param>
        /// <param name="from">The token index to start searching from.</param>
        private bool IsIndirectHL(List<Token> line, int from)
        {
            for (int i = from; i < line.Count - 2; i++)
                if (line[i].Type == TokenType.LeftParen && line[i + 1].Value.ToUpper() == "HL" && line[i + 2].Type == TokenType.RightParen)
                    return true;
            return false;
        }

        /// <summary>
        /// Returns <see langword="true"/> if the token at <paramref name="idx"/> represents
        /// an immediate operand (a number literal, identifier, or the <c>$</c> PC symbol).
        /// </summary>
        /// <param name="line">The tokens of a single instruction line.</param>
        /// <param name="idx">The index of the token to test.</param>
        private bool IsImmediate(List<Token> line, int idx)
        {
            if (idx >= line.Count) return false;
            var t = line[idx];
            return t.Type == TokenType.Number || t.Type == TokenType.Identifier || t.Type == TokenType.Dollar;
        }

        /// <summary>
        /// Returns <see langword="true"/> if the instruction on <paramref name="line"/>
        /// uses a register or indirect operand (e.g. <c>JP (HL)</c>, <c>JP HL</c>)
        /// rather than an absolute address.
        /// </summary>
        /// <param name="line">The tokens of a single instruction line.</param>
        private bool IsRegOrIndirect(List<Token> line)
        {
            for (int i = 1; i < line.Count; i++)
                if (line[i].Type == TokenType.LeftParen) return true;
            if (line.Count > 1 && line[1].Type == TokenType.Register) return true;
            return false;
        }

        /// <summary>
        /// Estimates the byte size of an <c>LD</c> instruction from its token list.
        /// </summary>
        /// <param name="line">The tokens of the LD instruction line (including the mnemonic).</param>
        /// <returns>The estimated size in bytes (1, 2, or 3).</returns>
        private int EstimateLDSize(List<Token> line)
        {
            if (HasIXIY(line)) return 3;

            // LD r,(HL+) — pseudo: LD r,(HL) + INC HL = 2 bytes
            if (line.Any(t => t.Type == TokenType.Plus)) return 2;

            bool hasParen = line.Any(t => t.Type == TokenType.LeftParen);
            if (hasParen) return 3;

            // LD rr,rr' — pseudo: two 8-bit LDs = 2 bytes
            if (line.Count >= 4)
            {
                bool bothReg16 = (line[1].Type == TokenType.Register || line[1].Type == TokenType.Identifier) &&
                                 (line[3].Type == TokenType.Register || line[3].Type == TokenType.Identifier) &&
                                 IsReg16Plain(line[1].Value) && IsReg16Plain(line[3].Value);
                if (bothReg16) return 2;
                return 3; // LD rr,nn or LD r,n
            }
            if (line.Count >= 3 && line[2].Type == TokenType.Number) return 2;
            return 1;
        }

        /// <summary>
        /// Counts the number of bytes that a <c>DB</c>/<c>DEFB</c>/<c>DEFM</c> directive
        /// will emit, accounting for string literals (one byte per character) and individual
        /// numeric values (one byte each).
        /// </summary>
        /// <param name="line">The tokens of the DB/DEFB/DEFM line (including the mnemonic).</param>
        /// <returns>The total number of bytes the directive will produce.</returns>
        private int CountDBSize(List<Token> line)
        {
            int count = 0;
            for (int i = 1; i < line.Count; i++)
            {
                if (line[i].Type == TokenType.Comma) continue;
                if (line[i].Type == TokenType.String) count += line[i].Value.Length;
                else count++;
            }
            return count;
        }

        /// <summary>
        /// Counts the number of bytes that a <c>DW</c>/<c>DEFW</c> directive will emit
        /// (two bytes per word value).
        /// </summary>
        /// <param name="line">The tokens of the DW/DEFW line (including the mnemonic).</param>
        /// <returns>The total number of bytes the directive will produce.</returns>
        private int CountDWSize(List<Token> line)
        {
            int count = 0;
            for (int i = 1; i < line.Count; i++)
                if (line[i].Type != TokenType.Comma) count += 2;
            return count;
        }

        /// <summary>
        /// Pass 2: walks the token stream, encodes each instruction to bytes, updates the
        /// symbol table with definitive addresses, builds the assembly listing, and
        /// records forward-reference patches.
        /// </summary>
        /// <param name="tokens">The flat token list produced by the <see cref="Lexer"/>.</param>
        /// <param name="source">The original source text, used to extract source lines for the listing.</param>
        private void Pass2(List<Token> tokens, string source)
        {
            _pc = _loadAddress;
            int i = 0;
            string pendingLabel = null;
            bool atLineStart = true; // true after a NewLine (or at start of file)

            while (i < tokens.Count && tokens[i].Type != TokenType.EOF)
            {
                var tok = tokens[i];

                if (tok.Type == TokenType.NewLine) { i++; atLineStart = true; continue; }

                if (tok.Type == TokenType.Identifier && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Colon)
                {
                    pendingLabel = tok.Value;
                    _symbols[tok.Value] = _pc;
                    i += 2;
                    atLineStart = false;
                    continue;
                }

                // EQU / SET without colon: IDENT EQU expr  or  IDENT SET expr
                if (tok.Type == TokenType.Identifier && i + 1 < tokens.Count &&
                    tokens[i + 1].Type == TokenType.Mnemonic &&
                    (tokens[i + 1].Value.Equals("EQU", StringComparison.OrdinalIgnoreCase) ||
                     tokens[i + 1].Value.Equals("SET", StringComparison.OrdinalIgnoreCase)))
                {
                    string symName = tok.Value;
                    i += 2; // skip name and EQU/SET
                    int exprIdx = i;
                    long val = EvaluateExpr(tokens, ref exprIdx, _pc, tok.Line);
                    _symbols[symName] = (ushort)val;
                    i = exprIdx;
                    while (i < tokens.Count && tokens[i].Type != TokenType.NewLine) i++;
                    atLineStart = false;
                    continue;
                }

                // 8080-style colonless label: identifier at start of line followed by mnemonic or NewLine
                if (atLineStart && tok.Type == TokenType.Identifier && i + 1 < tokens.Count &&
                    (tokens[i + 1].Type == TokenType.NewLine ||
                     tokens[i + 1].Type == TokenType.Mnemonic ||
                     tokens[i + 1].Type == TokenType.EOF))
                {
                    pendingLabel = tok.Value;
                    _symbols[tok.Value] = _pc;
                    i++; // skip just the label, keep the mnemonic for encoding
                    atLineStart = false;
                    continue;
                }

                if (tok.Type == TokenType.Mnemonic)
                {
                    ushort addrBefore = _pc;
                    var lineToks = CollectLine(tokens, ref i);
                    var bytes = EncodeInstruction(lineToks, tok.Line);
                    string listingLine = FormatListing(addrBefore, bytes, source, tok.Line);
                    _listing.Add(listingLine);
                    foreach (var b in bytes) { _output.Add(b); _pc++; }
                    pendingLabel = null;
                    atLineStart = false;
                    continue;
                }
                else if (tok.Type == TokenType.Identifier)
                {
                    // Bare identifier in instruction position = unknown mnemonic.
                    _errors.Add(new AssemblerError(tok.Line, $"Unknown mnemonic '{tok.Value}'"));
                    while (i < tokens.Count && tokens[i].Type != TokenType.NewLine) i++;
                }

                atLineStart = false;
                i++;
            }
        }

        /// <summary>
        /// Advances <paramref name="i"/> past all tokens up to (but not including) the next
        /// <see cref="TokenType.NewLine"/> or <see cref="TokenType.EOF"/> and returns the
        /// collected tokens as a list.
        /// </summary>
        /// <param name="tokens">The full token list.</param>
        /// <param name="i">The current token index; advanced in-place.</param>
        /// <returns>The tokens that form a single logical source line.</returns>
        private List<Token> CollectLine(List<Token> tokens, ref int i)
        {
            var line = new List<Token>();
            while (i < tokens.Count && tokens[i].Type != TokenType.NewLine && tokens[i].Type != TokenType.EOF)
                line.Add(tokens[i++]);
            return line;
        }

        /// <summary>
        /// Dispatches a single instruction token list to the appropriate encoder and returns
        /// the encoded bytes.  Adds an error and returns an empty array on failure.
        /// </summary>
        /// <param name="line">The tokens of a single instruction line (mnemonic first).</param>
        /// <param name="lineNum">The 1-based source line number, used for error reporting.</param>
        /// <returns>The encoded machine-code bytes for this instruction.</returns>
        private byte[] EncodeInstruction(List<Token> line, int lineNum)
        {
            if (line.Count == 0) return new byte[0];
            string mn = line[0].Value.ToUpper();

            try
            {
                switch (mn)
                {
                    case "NOP": return new byte[] { 0x00 };
                    case "HALT": return new byte[] { 0x76 };
                    case "DI": return new byte[] { 0xF3 };
                    case "EI": return new byte[] { 0xFB };
                    case "EXX": return new byte[] { 0xD9 };
                    case "RLCA": return new byte[] { 0x07 };
                    case "RRCA": return new byte[] { 0x0F };
                    case "RLA": return new byte[] { 0x17 };
                    case "RRA": return new byte[] { 0x1F };
                    case "DAA": return new byte[] { 0x27 };
                    case "CPL": return new byte[] { 0x2F };
                    case "SCF": return new byte[] { 0x37 };
                    case "CCF": return new byte[] { 0x3F };
                    case "NEG": return new byte[] { 0xED, 0x44 };
                    case "RETI": return new byte[] { 0xED, 0x4D };
                    case "RETN": return new byte[] { 0xED, 0x45 };
                    case "LDI": return new byte[] { 0xED, 0xA0 };
                    case "CPI": return new byte[] { 0xED, 0xA1 };
                    case "INI": return new byte[] { 0xED, 0xA2 };
                    case "OUTI": return new byte[] { 0xED, 0xA3 };
                    case "LDD": return new byte[] { 0xED, 0xA8 };
                    case "CPD": return new byte[] { 0xED, 0xA9 };
                    case "IND": return new byte[] { 0xED, 0xAA };
                    case "OUTD": return new byte[] { 0xED, 0xAB };
                    case "LDIR": return new byte[] { 0xED, 0xB0 };
                    case "CPIR": return new byte[] { 0xED, 0xB1 };
                    case "INIR": return new byte[] { 0xED, 0xB2 };
                    case "OTIR": return new byte[] { 0xED, 0xB3 };
                    case "LDDR": return new byte[] { 0xED, 0xB8 };
                    case "CPDR": return new byte[] { 0xED, 0xB9 };
                    case "INDR": return new byte[] { 0xED, 0xBA };
                    case "OTDR": return new byte[] { 0xED, 0xBB };

                    case "IM": return EncodeIM(line, lineNum);
                    case "LD": return EncodeLD(line, lineNum);
                    case "PUSH": return EncodePushPop(line, lineNum, true);
                    case "POP": return EncodePushPop(line, lineNum, false);
                    case "ADD": return EncodeADD(line, lineNum);
                    case "ADC": return EncodeADC(line, lineNum);
                    case "SUB": return EncodeALU(line, lineNum, 0x90, 0xD6, new byte[] { 0xED, 0x42 });
                    case "SBC": return EncodeSBC(line, lineNum);
                    case "AND": return EncodeALU(line, lineNum, 0xA0, 0xE6, null);
                    case "XOR": return EncodeALU(line, lineNum, 0xA8, 0xEE, null);
                    case "OR": return EncodeALU(line, lineNum, 0xB0, 0xF6, null);
                    case "CP": return EncodeALU(line, lineNum, 0xB8, 0xFE, null);
                    case "INC": return EncodeIncDec(line, lineNum, true);
                    case "DEC": return EncodeIncDec(line, lineNum, false);
                    case "JP": return EncodeJP(line, lineNum);
                    case "JR": return EncodeJR(line, lineNum);
                    case "CALL": return EncodeCALL(line, lineNum);
                    case "RET": return EncodeRET(line, lineNum);
                    case "DJNZ": return EncodeDJNZ(line, lineNum);
                    case "RST": return EncodeRST(line, lineNum);
                    case "EX": return EncodeEX(line, lineNum);
                    case "IN": return EncodeIN(line, lineNum);
                    case "OUT": return EncodeOUT(line, lineNum);
                    case "BIT": return EncodeBitOp(line, lineNum, 0x40);
                    case "RES": return EncodeBitOp(line, lineNum, 0x80);
                    case "SET": return EncodeBitOp(line, lineNum, 0xC0);
                    case "RL": return EncodeRotShift(line, lineNum, 0x10);
                    case "RLC": return EncodeRotShift(line, lineNum, 0x00);
                    case "RR": return EncodeRotShift(line, lineNum, 0x18);
                    case "RRC": return EncodeRotShift(line, lineNum, 0x08);
                    case "SLA": return EncodeRotShift(line, lineNum, 0x20);
                    case "SRA": return EncodeRotShift(line, lineNum, 0x28);
                    case "SRL": return EncodeRotShift(line, lineNum, 0x38);
                    case "SLL": return EncodeRotShift(line, lineNum, 0x30);

                    case "DB": case "DEFB": case "DEFM": return EncodeDB(line, lineNum);
                    case "DW": case "DEFW": return EncodeDW(line, lineNum);
                    case "DS": case "DEFS": return EncodeDS(line, lineNum);

                    case "ORG":
                        int idx = 1;
                        _pc = (ushort)EvaluateExpr(line, ref idx, _pc, lineNum);
                        // Also update _loadAddress if no bytes have been emitted yet
                        if (_output.Count == 0)
                            _loadAddress = _pc;
                        return new byte[0];

                    case "EQU":
                        // Handled via symbol table in Pass 1 / Pass 2 EQU block
                        return new byte[0];

                    case "DEFC":
                    {
                        // DEFC SYMBOL = expr — resolve in Pass 2 as well to keep symbol table current
                        int idx2 = 1;
                        if (idx2 < line.Count && line[idx2].Type == TokenType.Identifier)
                        {
                            string symName = line[idx2].Value;
                            idx2++;
                            if (idx2 < line.Count && line[idx2].Type == TokenType.Equals) idx2++;
                            long val = EvaluateExpr(line, ref idx2, _pc, lineNum);
                            _symbols[symName] = (ushort)val;
                        }
                        return new byte[0];
                    }

                    case "PUBLIC": case "EXTERN": case "GLOBAL":
                    case "MODULE": case "SECTION":
                        // Linkage/section directives — no code emitted
                        return new byte[0];

                    case "END":
                        return new byte[0];

                    // Meta-directives — silently ignore
                    case "IF": case "ELSE": case "ENDIF":
                    case "TITLE": case "PAGE": case "EJECT":
                    case "NAME": case "MACLIB": case "STKLN":
                        return new byte[0];

                    // ── Intel 8080 compatibility ────────────────────────────────────────
                    // The Z80 is binary-compatible with the 8080; all 8080 opcodes are
                    // valid Z80 opcodes.  We translate 8080 mnemonics to their Z80 bytes.

                    // MOV dst,src  →  LD dst,src  (0x40 + dst*8 + src, M=6)
                    case "MOV":
                    {
                        if (line.Count < 4) { _errors.Add(new AssemblerError(lineNum, "MOV requires two operands")); return new byte[0]; }
                        int dst = Reg8Code(line[1].Value);
                        int src = Reg8Code(line[3].Value);
                        if (dst == 6 && src == 6) { _errors.Add(new AssemblerError(lineNum, "MOV M,M is invalid")); return new byte[0]; }
                        return new byte[] { (byte)(0x40 | (dst << 3) | src) };
                    }

                    // MVI dst,n  →  LD dst,n  (0x06 + dst*8, n)
                    case "MVI":
                    {
                        if (line.Count < 4) { _errors.Add(new AssemblerError(lineNum, "MVI requires two operands")); return new byte[0]; }
                        int dst = Reg8Code(line[1].Value);
                        int exI = 3; byte n = (byte)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { (byte)(0x06 | (dst << 3)), n };
                    }

                    // LXI rp,nn  →  LD rp,nn  (0x01 + rp*16, lo, hi)
                    case "LXI":
                    {
                        if (line.Count < 4) { _errors.Add(new AssemblerError(lineNum, "LXI requires two operands")); return new byte[0]; }
                        int rp = Reg16Code8080(line[1].Value);
                        int exI = 3; ushort nn = (ushort)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { (byte)(0x01 | (rp << 4)), (byte)(nn & 0xFF), (byte)(nn >> 8) };
                    }

                    // LDA nn  →  LD A,(nn)  (0x3A, lo, hi)
                    case "LDA":
                    {
                        int exI = 1; ushort nn = (ushort)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0x3A, (byte)(nn & 0xFF), (byte)(nn >> 8) };
                    }

                    // STA nn  →  LD (nn),A  (0x32, lo, hi)
                    case "STA":
                    {
                        int exI = 1; ushort nn = (ushort)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0x32, (byte)(nn & 0xFF), (byte)(nn >> 8) };
                    }

                    // LHLD nn  →  LD HL,(nn)  (0x2A, lo, hi)
                    case "LHLD":
                    {
                        int exI = 1; ushort nn = (ushort)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0x2A, (byte)(nn & 0xFF), (byte)(nn >> 8) };
                    }

                    // SHLD nn  →  LD (nn),HL  (0x22, lo, hi)
                    case "SHLD":
                    {
                        int exI = 1; ushort nn = (ushort)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0x22, (byte)(nn & 0xFF), (byte)(nn >> 8) };
                    }

                    // LDAX rp  →  LD A,(rp)  BC=0x0A  DE=0x1A
                    case "LDAX":
                    {
                        string rp = line.Count > 1 ? line[1].Value.ToUpper() : "";
                        if (rp == "B" || rp == "BC") return new byte[] { 0x0A };
                        if (rp == "D" || rp == "DE") return new byte[] { 0x1A };
                        _errors.Add(new AssemblerError(lineNum, $"LDAX: invalid register pair '{rp}'")); return new byte[0];
                    }

                    // STAX rp  →  LD (rp),A  BC=0x02  DE=0x12
                    case "STAX":
                    {
                        string rp = line.Count > 1 ? line[1].Value.ToUpper() : "";
                        if (rp == "B" || rp == "BC") return new byte[] { 0x02 };
                        if (rp == "D" || rp == "DE") return new byte[] { 0x12 };
                        _errors.Add(new AssemblerError(lineNum, $"STAX: invalid register pair '{rp}'")); return new byte[0];
                    }

                    // XCHG  →  EX DE,HL  (0xEB)
                    case "XCHG": return new byte[] { 0xEB };

                    // XTHL  →  EX (SP),HL  (0xE3)
                    case "XTHL": return new byte[] { 0xE3 };

                    // SPHL  →  LD SP,HL  (0xF9)
                    case "SPHL": return new byte[] { 0xF9 };

                    // PCHL  →  JP (HL)  (0xE9)
                    case "PCHL": return new byte[] { 0xE9 };

                    // ADI n  →  ADD A,n  (0xC6, n)
                    case "ADI":
                    {
                        int exI = 1; byte n = (byte)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0xC6, n };
                    }

                    // ACI n  →  ADC A,n  (0xCE, n)
                    case "ACI":
                    {
                        int exI = 1; byte n = (byte)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0xCE, n };
                    }

                    // SUI n  →  SUB n  (0xD6, n)
                    case "SUI":
                    {
                        int exI = 1; byte n = (byte)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0xD6, n };
                    }

                    // SBB r/M  →  SBC A,r  (0x98 + src)
                    case "SBB":
                    {
                        if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "SBB requires operand")); return new byte[0]; }
                        int src = Reg8Code(line[1].Value);
                        return new byte[] { (byte)(0x98 | src) };
                    }

                    // SBI n  →  SBC A,n  (0xDE, n)
                    case "SBI":
                    {
                        int exI = 1; byte n = (byte)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0xDE, n };
                    }

                    // ANA r/M  →  AND r  (0xA0 + src)
                    case "ANA":
                    {
                        if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "ANA requires operand")); return new byte[0]; }
                        int src = Reg8Code(line[1].Value);
                        return new byte[] { (byte)(0xA0 | src) };
                    }

                    // ANI n  →  AND n  (0xE6, n)
                    case "ANI":
                    {
                        int exI = 1; byte n = (byte)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0xE6, n };
                    }

                    // ORA r/M  →  OR r  (0xB0 + src)
                    case "ORA":
                    {
                        if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "ORA requires operand")); return new byte[0]; }
                        int src = Reg8Code(line[1].Value);
                        return new byte[] { (byte)(0xB0 | src) };
                    }

                    // ORI n  →  OR n  (0xF6, n)
                    case "ORI":
                    {
                        int exI = 1; byte n = (byte)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0xF6, n };
                    }

                    // XRA r/M  →  XOR r  (0xA8 + src)
                    case "XRA":
                    {
                        if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "XRA requires operand")); return new byte[0]; }
                        int src = Reg8Code(line[1].Value);
                        return new byte[] { (byte)(0xA8 | src) };
                    }

                    // XRI n  →  XOR n  (0xEE, n)
                    case "XRI":
                    {
                        int exI = 1; byte n = (byte)EvaluateExpr(line, ref exI, _pc, lineNum);
                        return new byte[] { 0xEE, n };
                    }

                    // CMP r/M  →  CP r  (0xB8 + src)
                    case "CMP":
                    {
                        if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "CMP requires operand")); return new byte[0]; }
                        int src = Reg8Code(line[1].Value);
                        return new byte[] { (byte)(0xB8 | src) };
                    }

                    // CMA  →  CPL  (0x2F)
                    case "CMA": return new byte[] { 0x2F };

                    // STC  →  SCF  (0x37)
                    case "STC": return new byte[] { 0x37 };

                    // CMC  →  CCF  (0x3F)
                    case "CMC": return new byte[] { 0x3F };

                    // HLT  →  HALT  (0x76)
                    case "HLT": return new byte[] { 0x76 };

                    // INR r/M  →  INC r  (0x04 + r*8)
                    case "INR":
                    {
                        if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "INR requires operand")); return new byte[0]; }
                        int r = Reg8Code(line[1].Value);
                        return new byte[] { (byte)(0x04 | (r << 3)) };
                    }

                    // DCR r/M  →  DEC r  (0x05 + r*8)
                    case "DCR":
                    {
                        if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "DCR requires operand")); return new byte[0]; }
                        int r = Reg8Code(line[1].Value);
                        return new byte[] { (byte)(0x05 | (r << 3)) };
                    }

                    // INX rp  →  INC rp  (0x03 + rp*16)
                    case "INX":
                    {
                        if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "INX requires operand")); return new byte[0]; }
                        int rp = Reg16Code8080(line[1].Value);
                        return new byte[] { (byte)(0x03 | (rp << 4)) };
                    }

                    // DCX rp  →  DEC rp  (0x0B + rp*16)
                    case "DCX":
                    {
                        if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "DCX requires operand")); return new byte[0]; }
                        int rp = Reg16Code8080(line[1].Value);
                        return new byte[] { (byte)(0x0B | (rp << 4)) };
                    }

                    // DAD rp  →  ADD HL,rp  (0x09 + rp*16)
                    case "DAD":
                    {
                        if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "DAD requires operand")); return new byte[0]; }
                        int rp = Reg16Code8080(line[1].Value);
                        return new byte[] { (byte)(0x09 | (rp << 4)) };
                    }

                    // RAL  →  RLA  (0x17)
                    case "RAL": return new byte[] { 0x17 };

                    // RAR  →  RRA  (0x1F)
                    case "RAR": return new byte[] { 0x1F };

                    // JMP nn  →  JP nn  (0xC3, lo, hi)
                    case "JMP": return Encode8080Jump(line, lineNum, 0xC3);

                    // Conditional jumps  (0xC2/CA/D2/DA/E2/EA/F2/FA)
                    case "JNZ": return Encode8080Jump(line, lineNum, 0xC2);
                    case "JZ":  return Encode8080Jump(line, lineNum, 0xCA);
                    case "JNC": return Encode8080Jump(line, lineNum, 0xD2);
                    case "JC":  return Encode8080Jump(line, lineNum, 0xDA);
                    case "JPO": return Encode8080Jump(line, lineNum, 0xE2);
                    case "JPE": return Encode8080Jump(line, lineNum, 0xEA);
                    case "JM":  return Encode8080Jump(line, lineNum, 0xFA);

                    // Conditional calls  (0xC4/CC/D4/DC/E4/EC/F4/FC)
                    case "CNZ": return Encode8080Jump(line, lineNum, 0xC4);
                    case "CZ":  return Encode8080Jump(line, lineNum, 0xCC);
                    case "CNC": return Encode8080Jump(line, lineNum, 0xD4);
                    case "CC":  return Encode8080Jump(line, lineNum, 0xDC);
                    case "CPO": return Encode8080Jump(line, lineNum, 0xE4);
                    case "CPE": return Encode8080Jump(line, lineNum, 0xEC);
                    case "CM":  return Encode8080Jump(line, lineNum, 0xFC);

                    // Conditional returns  (0xC0/C8/D0/D8/E0/E8/F0/F8)
                    case "RNZ": return new byte[] { 0xC0 };
                    case "RZ":  return new byte[] { 0xC8 };
                    case "RNC": return new byte[] { 0xD0 };
                    case "RC":  return new byte[] { 0xD8 };
                    case "RPO": return new byte[] { 0xE0 };
                    case "RPE": return new byte[] { 0xE8 };
                    case "RP":  return new byte[] { 0xF0 };
                    case "RM":  return new byte[] { 0xF8 };

                    default:
                        // Silently ignore dot-prefixed processor directives (.Z80, .Z180, .8080, etc.)
                        if (mn.StartsWith('.'))
                            return new byte[0];
                        _errors.Add(new AssemblerError(lineNum, $"Unknown mnemonic '{mn}'"));
                        return new byte[0];
                }
            }
            catch (Exception ex)
            {
                _errors.Add(new AssemblerError(lineNum, ex.Message));
                return new byte[0];
            }
        }

        // ===== Instruction Encoders =====

        /// <summary>Encodes an <c>IM</c> (interrupt mode) instruction.</summary>
        /// <param name="line">Instruction tokens (mnemonic + mode 0/1/2).</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>Two-byte <c>ED</c>-prefixed opcode.</returns>
        private byte[] EncodeIM(List<Token> line, int lineNum)
        {
            if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, "IM requires operand")); return new byte[0]; }
            int mode = (int)ParseNum(line[1].Value);
            switch (mode)
            {
                case 0: return new byte[] { 0xED, 0x46 };
                case 1: return new byte[] { 0xED, 0x56 };
                case 2: return new byte[] { 0xED, 0x5E };
                default: _errors.Add(new AssemblerError(lineNum, $"Invalid IM mode {mode}")); return new byte[0];
            }
        }

        /// <summary>
        /// Encodes an <c>LD</c> instruction, handling all Z80 LD variants including
        /// register-to-register, immediate, indirect, 16-bit, IX/IY-indexed, and special
        /// registers I and R.
        /// </summary>
        /// <param name="line">Instruction tokens (mnemonic, destination, comma, source).</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The encoded bytes for the LD instruction.</returns>
        private byte[] EncodeLD(List<Token> line, int lineNum)
        {
            // Parse: LD dst,src
            var (dst, src) = SplitOperands(line);

            bool dstInd = IsIndirect(dst);
            bool srcInd = IsIndirect(src);

            string dstReg = dstInd ? StripParens(dst) : GetReg(dst);
            string srcReg = srcInd ? StripParens(src) : GetReg(src);

            // LD rr, rr'  — Zilog assembler pseudo-op: expands to two 8-bit LD instructions.
            // e.g. LD HL,BC → LD H,B ; LD L,C
            if (!dstInd && !srcInd && IsReg16Plain(dstReg) && IsReg16Plain(srcReg))
            {
                var (dstHi, dstLo) = SplitReg16(dstReg);
                var (srcHi, srcLo) = SplitReg16(srcReg);
                return new byte[]
                {
                    (byte)(0x40 | (Reg8Code(dstHi) << 3) | Reg8Code(srcHi)),
                    (byte)(0x40 | (Reg8Code(dstLo) << 3) | Reg8Code(srcLo))
                };
            }

            // LD r,(HL+)  — Zilog assembler shorthand: LD r,(HL) then INC HL (2 bytes)
            if (!dstInd && IsReg8(dstReg) && src.Trim().ToUpper() == "(HL+)")
            {
                return new byte[]
                {
                    (byte)(0x46 | (Reg8Code(dstReg) << 3)),  // LD r,(HL)
                    0x23                                       // INC HL
                };
            }

            // LD r, r'
            if (!dstInd && !srcInd && IsReg8(dstReg) && IsReg8(srcReg))
            {
                if (dstReg == "HL" || srcReg == "HL") goto complex;
                return new byte[] { (byte)(0x40 | (Reg8Code(dstReg) << 3) | Reg8Code(srcReg)) };
            }

        complex:
            // LD r, n (8-bit immediate)
            if (!dstInd && IsReg8(dstReg) && !srcInd && !IsReg8(srcReg) && !IsReg16(srcReg))
            {
                int n = EvalOperand(src, lineNum);
                return new byte[] { (byte)(0x06 | (Reg8Code(dstReg) << 3)), (byte)n };
            }

            // LD rr, nn (16-bit immediate)
            if (!dstInd && IsReg16(dstReg) && !srcInd && !IsReg8(srcReg) && !IsReg16(srcReg))
            {
                int nn = EvalOperand(src, lineNum);
                byte[] prefix = IXIYPrefix(dstReg);
                string baseReg = BaseReg16(dstReg);
                return Concat(prefix, new byte[] { (byte)(0x01 | (Reg16Code(baseReg) << 4)), (byte)(nn & 0xFF), (byte)(nn >> 8) });
            }

            // LD A, (BC) / (DE)
            if (!dstInd && dstReg == "A" && srcInd)
            {
                string sr = srcReg.ToUpper();
                if (sr == "BC") return new byte[] { 0x0A };
                if (sr == "DE") return new byte[] { 0x1A };
            }

            // LD (BC), A / (DE), A
            if (dstInd && !srcInd && srcReg == "A")
            {
                string dr = dstReg.ToUpper();
                if (dr == "BC") return new byte[] { 0x02 };
                if (dr == "DE") return new byte[] { 0x12 };
            }

            // LD A, (nn) / LD (nn), A
            if (!dstInd && dstReg == "A" && srcInd && !IsReg16(srcReg) && !IsReg8(srcReg))
            {
                int nn = EvalOperand(srcReg, lineNum);
                return new byte[] { 0x3A, (byte)(nn & 0xFF), (byte)(nn >> 8) };
            }
            if (dstInd && !IsReg16(dstReg) && !IsReg8(dstReg) && !srcInd && srcReg == "A")
            {
                int nn = EvalOperand(dstReg, lineNum);
                return new byte[] { 0x32, (byte)(nn & 0xFF), (byte)(nn >> 8) };
            }

            // LD HL, (nn) / LD (nn), HL
            if (!dstInd && dstReg == "HL" && srcInd)
            {
                int nn = EvalOperand(srcReg, lineNum);
                return new byte[] { 0x2A, (byte)(nn & 0xFF), (byte)(nn >> 8) };
            }
            if (dstInd && !srcInd && srcReg == "HL")
            {
                int nn = EvalOperand(dstReg, lineNum);
                return new byte[] { 0x22, (byte)(nn & 0xFF), (byte)(nn >> 8) };
            }

            // LD rr, (nn) / LD (nn), rr  [ED prefix]
            if (!dstInd && IsReg16(dstReg) && srcInd && !IsReg8(srcReg) && !IsReg16(srcReg))
            {
                int nn = EvalOperand(srcReg, lineNum);
                byte[] pre = IXIYPrefix(dstReg);
                string br = BaseReg16(dstReg);
                if (br == "HL" && pre.Length == 0) return new byte[] { 0x2A, (byte)(nn & 0xFF), (byte)(nn >> 8) };
                return Concat(pre, Concat(new byte[] { 0xED, (byte)(0x4B | (Reg16Code(br) << 4)) }, new byte[] { (byte)(nn & 0xFF), (byte)(nn >> 8) }));
            }
            if (dstInd && !IsReg8(dstReg) && !IsReg16(dstReg) && !srcInd && IsReg16(srcReg))
            {
                int nn = EvalOperand(dstReg, lineNum);
                byte[] pre = IXIYPrefix(srcReg);
                string br = BaseReg16(srcReg);
                if (br == "HL" && pre.Length == 0) return new byte[] { 0x22, (byte)(nn & 0xFF), (byte)(nn >> 8) };
                return Concat(pre, Concat(new byte[] { 0xED, (byte)(0x43 | (Reg16Code(br) << 4)) }, new byte[] { (byte)(nn & 0xFF), (byte)(nn >> 8) }));
            }

            // LD SP, HL / IX / IY
            if (!dstInd && dstReg == "SP" && !srcInd && (srcReg == "HL" || srcReg == "IX" || srcReg == "IY"))
            {
                byte[] pre = IXIYPrefix(srcReg);
                return Concat(pre, new byte[] { 0xF9 });
            }

            // LD A, I / LD A, R
            if (!dstInd && dstReg == "A" && !srcInd && (srcReg == "I" || srcReg == "R"))
            {
                return new byte[] { 0xED, srcReg == "I" ? (byte)0x57 : (byte)0x5F };
            }
            if (!dstInd && (dstReg == "I" || dstReg == "R") && !srcInd && srcReg == "A")
            {
                return new byte[] { 0xED, dstReg == "I" ? (byte)0x47 : (byte)0x4F };
            }

            // LD r, (HL)
            if (!dstInd && IsReg8(dstReg) && srcInd && srcReg == "HL")
                return new byte[] { (byte)(0x46 | (Reg8Code(dstReg) << 3)) };

            // LD (HL), r
            if (dstInd && dstReg == "HL" && !srcInd && IsReg8(srcReg))
                return new byte[] { (byte)(0x70 | Reg8Code(srcReg)) };

            // LD (HL), n
            if (dstInd && dstReg == "HL" && !srcInd && !IsReg8(srcReg))
            {
                int n = EvalOperand(src, lineNum);
                return new byte[] { 0x36, (byte)n };
            }

            // LD r, (IX/IY+d)
            if (!dstInd && IsReg8(dstReg))
            {
                var (prefix, disp) = ParseIXIYDisp(src);
                if (prefix != 0)
                    return new byte[] { prefix, (byte)(0x46 | (Reg8Code(dstReg) << 3)), (byte)(sbyte)disp };
            }

            // LD (IX/IY+d), r
            if (dstInd)
            {
                var (prefix, disp) = ParseIXIYDisp(dst);
                if (prefix != 0)
                {
                    if (!srcInd && IsReg8(srcReg))
                        return new byte[] { prefix, (byte)(0x70 | Reg8Code(srcReg)), (byte)(sbyte)disp };
                    // LD (IX/IY+d), n
                    int n = EvalOperand(src, lineNum);
                    return new byte[] { prefix, 0x36, (byte)(sbyte)disp, (byte)n };
                }
            }

            _errors.Add(new AssemblerError(lineNum, $"Invalid LD operands: {String.Join(" ", line.Select(t => t.Value))}"));
            return new byte[0];
        }

        /// <summary>Encodes a <c>PUSH</c> or <c>POP</c> instruction for any 16-bit register pair including IX and IY.</summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <param name="isPush"><see langword="true"/> for PUSH, <see langword="false"/> for POP.</param>
        /// <returns>The encoded bytes (1 byte for standard pairs, 2 bytes for IX/IY).</returns>
        private byte[] EncodePushPop(List<Token> line, int lineNum, bool isPush)
        {
            string reg = line.Count > 1 ? line[1].Value.ToUpper() : "";
            byte[] pre = IXIYPrefix(reg);
            string br = BaseReg16(reg);
            byte baseOp = isPush ? (byte)0xC5 : (byte)0xC1;
            int code = Reg16PushCode(br);
            return Concat(pre, new byte[] { (byte)(baseOp | (code << 4)) });
        }

        /// <summary>
        /// Encodes an <c>ADD</c> instruction, supporting <c>ADD A,r</c>, <c>ADD A,n</c>,
        /// <c>ADD HL,rr</c>, and <c>ADD IX/IY,rr</c>.
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The encoded bytes.</returns>
        private byte[] EncodeADD(List<Token> line, int lineNum)
        {
            var (dst, src) = SplitOperands(line);
            string dstR = GetReg(dst);
            string srcR = GetReg(src);

            // 8080 single-operand form: ADD r  (implicit accumulator destination)
            if (src == "")
            {
                if (IsReg8(dstR)) return new byte[] { (byte)(0x80 | Reg8Code(dstR)) };
                if (dstR == "(HL)" || dstR == "M") return new byte[] { 0x86 };
                int n = EvalOperand(dst, lineNum);
                return new byte[] { 0xC6, (byte)n };
            }

            if (dstR == "A")
            {
                if (IsReg8(srcR)) return new byte[] { (byte)(0x80 | Reg8Code(srcR)) };
                if (srcR == "(HL)") return new byte[] { 0x86 };
                var (pre, d) = ParseIXIYDisp(src);
                if (pre != 0) return new byte[] { pre, 0x86, (byte)(sbyte)d };
                int n = EvalOperand(src, lineNum);
                return new byte[] { 0xC6, (byte)n };
            }
            if (dstR == "HL")
            {
                byte[] pre = IXIYPrefix(dstR);
                string bs = BaseReg16(srcR);
                return Concat(pre, new byte[] { (byte)(0x09 | (Reg16Code(bs) << 4)) });
            }
            if (dstR == "IX" || dstR == "IY")
            {
                byte[] pre = IXIYPrefix(dstR);
                string bs = BaseReg16(srcR);
                return Concat(pre, new byte[] { (byte)(0x09 | (Reg16Code(bs) << 4)) });
            }
            _errors.Add(new AssemblerError(lineNum, "Invalid ADD operands")); return new byte[0];
        }

        /// <summary>Encodes an <c>ADC</c> instruction (<c>ADC A,r</c>, <c>ADC A,n</c>, or <c>ADC HL,rr</c>).</summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The encoded bytes.</returns>
        private byte[] EncodeADC(List<Token> line, int lineNum)
        {
            var (dst, src) = SplitOperands(line);
            string dstR = GetReg(dst);
            string srcR = GetReg(src);

            // 8080 single-operand form: ADC r  (implicit accumulator destination)
            if (src == "")
            {
                if (IsReg8(dstR)) return new byte[] { (byte)(0x88 | Reg8Code(dstR)) };
                if (dstR == "(HL)" || dstR == "M") return new byte[] { 0x8E };
                int n = EvalOperand(dst, lineNum);
                return new byte[] { 0xCE, (byte)n };
            }

            if (dstR == "A")
            {
                if (IsReg8(srcR)) return new byte[] { (byte)(0x88 | Reg8Code(srcR)) };
                int n = EvalOperand(src, lineNum);
                return new byte[] { 0xCE, (byte)n };
            }
            if (dstR == "HL")
            {
                return new byte[] { 0xED, (byte)(0x4A | (Reg16Code(srcR) << 4)) };
            }
            _errors.Add(new AssemblerError(lineNum, "Invalid ADC operands")); return new byte[0];
        }

        /// <summary>Encodes an <c>SBC</c> instruction (<c>SBC A,r</c>, <c>SBC A,n</c>, or <c>SBC HL,rr</c>).</summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The encoded bytes.</returns>
        private byte[] EncodeSBC(List<Token> line, int lineNum)
        {
            var (dst, src) = SplitOperands(line);
            string dstR = GetReg(dst);
            string srcR = GetReg(src);
            if (dstR == "A")
            {
                if (IsReg8(srcR)) return new byte[] { (byte)(0x98 | Reg8Code(srcR)) };
                int n = EvalOperand(src, lineNum);
                return new byte[] { 0xDE, (byte)n };
            }
            if (dstR == "HL")
            {
                return new byte[] { 0xED, (byte)(0x42 | (Reg16Code(srcR) << 4)) };
            }
            _errors.Add(new AssemblerError(lineNum, "Invalid SBC operands")); return new byte[0];
        }

        /// <summary>
        /// Generic encoder for single-operand ALU instructions:
        /// <c>SUB</c>, <c>AND</c>, <c>OR</c>, <c>XOR</c>, and <c>CP</c>.
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <param name="regBase">Base opcode for the register form (e.g. <c>0xA0</c> for AND).</param>
        /// <param name="immOp">Opcode for the immediate form (e.g. <c>0xE6</c> for AND n).</param>
        /// <param name="hl16op">Opcode bytes for the 16-bit HL form, or <see langword="null"/> if not applicable.</param>
        /// <returns>The encoded bytes.</returns>
        private byte[] EncodeALU(List<Token> line, int lineNum, byte regBase, byte immOp, byte[] hl16op)
        {
            // One or two operands; if two, first is A
            List<Token> operandTokens = line.Skip(1).ToList();
            // If first token is A and there's a comma, skip it
            string src;
            if (operandTokens.Count >= 2 && operandTokens[0].Value.ToUpper() == "A" && operandTokens[1].Type == TokenType.Comma)
                src = TokensToStr(operandTokens.Skip(2).ToList());
            else
                src = TokensToStr(operandTokens);

            string srcR = GetReg(src);
            if (IsReg8(srcR)) return new byte[] { (byte)(regBase | Reg8Code(srcR)) };
            if (srcR == "(HL)") return new byte[] { (byte)(regBase | 0x06) };
            var (pre, d) = ParseIXIYDisp(src);
            if (pre != 0) return new byte[] { pre, (byte)(regBase | 0x06), (byte)(sbyte)d };
            int n = EvalOperand(src, lineNum);
            return new byte[] { immOp, (byte)n };
        }

        /// <summary>Encodes an <c>INC</c> or <c>DEC</c> instruction for 8-bit registers, 16-bit pairs, (HL), and IX/IY-indexed.</summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <param name="isInc"><see langword="true"/> for INC, <see langword="false"/> for DEC.</param>
        /// <returns>The encoded bytes.</returns>
        private byte[] EncodeIncDec(List<Token> line, int lineNum, bool isInc)
        {
            string op = TokensToStr(line.Skip(1).ToList());
            string reg = GetReg(op);
            bool ind = IsIndirect(line.Skip(1).ToList());

            if (ind)
            {
                string r = StripParens(op);
                if (r.ToUpper() == "HL") return new byte[] { isInc ? (byte)0x34 : (byte)0x35 };
                var (pre, d) = ParseIXIYDisp(op);
                if (pre != 0) return new byte[] { pre, isInc ? (byte)0x34 : (byte)0x35, (byte)(sbyte)d };
            }

            if (IsReg8(reg))
                return new byte[] { (byte)((isInc ? 0x04 : 0x05) | (Reg8Code(reg) << 3)) };

            if (IsReg16(reg))
            {
                byte[] pre = IXIYPrefix(reg);
                string br = BaseReg16(reg);
                return Concat(pre, new byte[] { (byte)((isInc ? 0x03 : 0x0B) | (Reg16Code(br) << 4)) });
            }

            _errors.Add(new AssemblerError(lineNum, $"Invalid INC/DEC operand: {op}")); return new byte[0];
        }

        /// <summary>
        /// Encodes a <c>JP</c> instruction: unconditional (<c>JP nn</c>), conditional
        /// (<c>JP cc,nn</c>), or register-indirect (<c>JP (HL/IX/IY)</c>).
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The encoded bytes.</returns>
        private byte[] EncodeJP(List<Token> line, int lineNum)
        {
            // JP (HL), JP (IX), JP (IY) - indirect
            // JP cc, nn or JP nn
            var operands = SplitMultiOperands(line);

            if (operands.Count == 1)
            {
                string op = operands[0];
                string r = GetReg(op);
                if (IsIndirect(op))
                {
                    string inner = StripParens(op).ToUpper();
                    if (inner == "HL") return new byte[] { 0xE9 };
                    if (inner == "IX") return new byte[] { 0xDD, 0xE9 };
                    if (inner == "IY") return new byte[] { 0xFD, 0xE9 };
                }
                int nn = EvalOperand(op, lineNum);
                return new byte[] { 0xC3, (byte)(nn & 0xFF), (byte)(nn >> 8) };
            }
            else
            {
                string cc = operands[0].ToUpper();
                int nn = EvalOperand(operands[1], lineNum);
                byte op = (byte)(0xC2 | (CondCode(cc) << 3));
                return new byte[] { op, (byte)(nn & 0xFF), (byte)(nn >> 8) };
            }
        }

        /// <summary>
        /// Encodes a <c>JR</c> (relative jump) instruction: unconditional or conditional
        /// (NZ, Z, NC, C only).  The displacement is relative to the byte following the instruction.
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The two-byte encoded instruction.</returns>
        private byte[] EncodeJR(List<Token> line, int lineNum)
        {
            var operands = SplitMultiOperands(line);
            if (operands.Count == 1)
            {
                int target = EvalOperand(operands[0], lineNum);
                int disp = target - (_pc + 2);
                return new byte[] { 0x18, (byte)(sbyte)disp };
            }
            else
            {
                string cc = operands[0].ToUpper();
                int target = EvalOperand(operands[1], lineNum);
                int disp = target - (_pc + 2);
                byte[] ccMap = { 0x20, 0x28, 0x30, 0x38 }; // NZ Z NC C
                int ccIdx = new[] { "NZ", "Z", "NC", "C" }.ToList().IndexOf(cc);
                if (ccIdx < 0) { _errors.Add(new AssemblerError(lineNum, $"Invalid JR condition: {cc}")); return new byte[0]; }
                return new byte[] { ccMap[ccIdx], (byte)(sbyte)disp };
            }
        }

        /// <summary>
        /// Encodes a <c>CALL</c> instruction: unconditional (<c>CALL nn</c>) or conditional
        /// (<c>CALL cc,nn</c>).
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The three-byte encoded instruction.</returns>
        private byte[] EncodeCALL(List<Token> line, int lineNum)
        {
            var operands = SplitMultiOperands(line);
            if (operands.Count == 1)
            {
                int nn = EvalOperand(operands[0], lineNum);
                return new byte[] { 0xCD, (byte)(nn & 0xFF), (byte)(nn >> 8) };
            }
            else
            {
                string cc = operands[0].ToUpper();
                int nn = EvalOperand(operands[1], lineNum);
                return new byte[] { (byte)(0xC4 | (CondCode(cc) << 3)), (byte)(nn & 0xFF), (byte)(nn >> 8) };
            }
        }

        /// <summary>
        /// Encodes a <c>RET</c> instruction: unconditional (<c>RET</c>) or conditional
        /// (<c>RET cc</c>) for all eight condition codes.
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The one-byte encoded instruction.</returns>
        private byte[] EncodeRET(List<Token> line, int lineNum)
        {
            if (line.Count == 1) return new byte[] { 0xC9 };
            string cc = line[1].Value.ToUpper();
            return new byte[] { (byte)(0xC0 | (CondCode(cc) << 3)) };
        }

        /// <summary>
        /// Encodes a <c>DJNZ</c> instruction: decrements B and jumps to the target label
        /// if B is non-zero (2-byte relative offset from the byte following the instruction).
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The two-byte encoded instruction.</returns>
        private byte[] EncodeDJNZ(List<Token> line, int lineNum)
        {
            int target = EvalOperand(TokensToStr(line.Skip(1).ToList()), lineNum);
            int disp = target - (_pc + 2);
            return new byte[] { 0x10, (byte)(sbyte)disp };
        }

        /// <summary>
        /// Encodes an <c>RST</c> (restart) instruction.  The operand is masked to the eight
        /// valid restart vectors (0x00, 0x08, …, 0x38).
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The single-byte encoded instruction.</returns>
        private byte[] EncodeRST(List<Token> line, int lineNum)
        {
            int n = EvalOperand(TokensToStr(line.Skip(1).ToList()), lineNum);
            return new byte[] { (byte)(0xC7 | (n & 0x38)) };
        }

        /// <summary>
        /// Encodes an <c>EX</c> instruction: <c>EX AF,AF'</c>, <c>EX DE,HL</c>,
        /// <c>EX (SP),HL</c>, <c>EX (SP),IX</c>, or <c>EX (SP),IY</c>.
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The encoded bytes.</returns>
        private byte[] EncodeEX(List<Token> line, int lineNum)
        {
            var (dst, src) = SplitOperands(line);
            string dR = GetReg(dst).ToUpper();
            string sR = GetReg(src).ToUpper();
            if (dR == "AF" && sR == "AF'") return new byte[] { 0x08 };
            if (dR == "DE" && sR == "HL") return new byte[] { 0xEB };
            if (IsIndirect(dst) && GetReg(StripParens(dst)) == "SP")
            {
                if (sR == "HL") return new byte[] { 0xE3 };
                if (sR == "IX") return new byte[] { 0xDD, 0xE3 };
                if (sR == "IY") return new byte[] { 0xFD, 0xE3 };
            }
            _errors.Add(new AssemblerError(lineNum, "Invalid EX operands")); return new byte[0];
        }

        /// <summary>
        /// Encodes an <c>IN</c> instruction: <c>IN A,(n)</c> (port from immediate address) or
        /// <c>IN r,(C)</c> (port via register C using the ED prefix).
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The encoded bytes.</returns>
        private byte[] EncodeIN(List<Token> line, int lineNum)
        {
            var (dst, src) = SplitOperands(line);
            string dR = GetReg(dst).ToUpper();
            string sInner = StripParens(src).ToUpper();

            if (sInner == "C" || sInner == "BC")
            {
                if (IsReg8(dR)) return new byte[] { 0xED, (byte)(0x40 | (Reg8Code(dR) << 3)) };
            }
            int n = EvalOperand(sInner, lineNum);
            return new byte[] { 0xDB, (byte)n };
        }

        /// <summary>
        /// Encodes an <c>OUT</c> instruction: <c>OUT (n),A</c> (port to immediate address) or
        /// <c>OUT (C),r</c> (port via register C using the ED prefix).
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The encoded bytes.</returns>
        private byte[] EncodeOUT(List<Token> line, int lineNum)
        {
            var (dst, src) = SplitOperands(line);
            string dInner = StripParens(dst).ToUpper();
            string sR = GetReg(src).ToUpper();

            if (dInner == "C" || dInner == "BC")
            {
                if (IsReg8(sR)) return new byte[] { 0xED, (byte)(0x41 | (Reg8Code(sR) << 3)) };
            }
            int n = EvalOperand(dInner, lineNum);
            return new byte[] { 0xD3, (byte)n };
        }

        /// <summary>
        /// Encodes a <c>BIT</c>, <c>SET</c>, or <c>RES</c> instruction, including
        /// IX/IY-indexed variants with a signed 8-bit displacement.
        /// </summary>
        /// <param name="line">Instruction tokens (mnemonic, bit number, operand).</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <param name="baseOp">Base CB-prefix sub-opcode: <c>0x40</c> BIT, <c>0x80</c> RES, <c>0xC0</c> SET.</param>
        /// <returns>The encoded bytes (2 bytes for register/HL, 4 bytes for IX/IY-indexed).</returns>
        private byte[] EncodeBitOp(List<Token> line, int lineNum, byte baseOp)
        {
            var operands = SplitMultiOperands(line);
            int bit = (int)ParseNum(operands[0]);
            string op = operands[1];

            bool ind = IsIndirect(op);
            string r = ind ? StripParens(op) : GetReg(op);

            var (pre, d) = ParseIXIYDisp(op);
            if (pre != 0)
            {
                byte regCode = operands.Count > 2 ? (byte)Reg8Code(operands[2]) : (byte)0x06;
                return new byte[] { pre, 0xCB, (byte)(sbyte)d, (byte)(baseOp | (bit << 3) | regCode) };
            }

            if (ind && r.ToUpper() == "HL")
                return new byte[] { 0xCB, (byte)(baseOp | (bit << 3) | 0x06) };

            return new byte[] { 0xCB, (byte)(baseOp | (bit << 3) | Reg8Code(r)) };
        }

        /// <summary>
        /// Encodes a rotate/shift instruction (<c>RL</c>, <c>RR</c>, <c>RLC</c>, <c>RRC</c>,
        /// <c>SLA</c>, <c>SRA</c>, <c>SRL</c>, <c>SLL</c>), including IX/IY-indexed variants.
        /// </summary>
        /// <param name="line">Instruction tokens.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <param name="baseOp">CB-prefix sub-opcode identifying the specific rotation/shift.</param>
        /// <returns>The encoded bytes (2 bytes for register/HL, 4 bytes for IX/IY-indexed).</returns>
        private byte[] EncodeRotShift(List<Token> line, int lineNum, byte baseOp)
        {
            string op = TokensToStr(line.Skip(1).ToList());
            bool ind = IsIndirect(op);
            string r = ind ? StripParens(op) : GetReg(op);

            var (pre, d) = ParseIXIYDisp(op);
            if (pre != 0)
                return new byte[] { pre, 0xCB, (byte)(sbyte)d, (byte)(baseOp | 0x06) };

            if (ind && r.ToUpper() == "HL")
                return new byte[] { 0xCB, (byte)(baseOp | 0x06) };

            return new byte[] { 0xCB, (byte)(baseOp | Reg8Code(r)) };
        }

        /// <summary>
        /// Encodes a <c>DB</c>/<c>DEFB</c>/<c>DEFM</c> directive: each operand is written as
        /// one raw byte; string literals are expanded to their ASCII bytes.
        /// </summary>
        /// <param name="line">Instruction tokens (mnemonic followed by comma-separated values/strings).</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The raw data bytes.</returns>
        private byte[] EncodeDB(List<Token> line, int lineNum)
        {
            var result = new List<byte>();
            foreach (var tok in line.Skip(1))
            {
                if (tok.Type == TokenType.Comma) continue;
                if (tok.Type == TokenType.String)
                    result.AddRange(Encoding.ASCII.GetBytes(tok.Value));
                else
                    result.Add((byte)EvalOperand(tok.Value, lineNum));
            }
            return result.ToArray();
        }

        /// <summary>
        /// Encodes a <c>DW</c>/<c>DEFW</c> directive: each operand is written as a
        /// 16-bit little-endian word (two bytes, low byte first).
        /// </summary>
        /// <param name="line">Instruction tokens (mnemonic followed by comma-separated 16-bit values).</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>The raw data bytes (two bytes per value).</returns>
        private byte[] EncodeDW(List<Token> line, int lineNum)
        {
            var result = new List<byte>();
            foreach (var tok in line.Skip(1))
            {
                if (tok.Type == TokenType.Comma) continue;
                int v = EvalOperand(tok.Value, lineNum);
                result.Add((byte)(v & 0xFF));
                result.Add((byte)(v >> 8));
            }
            return result.ToArray();
        }

        /// <summary>
        /// Encodes a <c>DS</c>/<c>DEFS</c> directive: allocates <em>count</em> bytes, all
        /// initialised to the optional fill value (default 0x00).
        /// </summary>
        /// <param name="line">Instruction tokens: mnemonic, count [, comma, fill].</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <returns>An array of <em>count</em> bytes set to the fill value.</returns>
        private byte[] EncodeDS(List<Token> line, int lineNum)
        {
            if (line.Count < 2) return new byte[0];
            int count = EvalOperand(line[1].Value, lineNum);
            byte fill = line.Count >= 4 ? (byte)EvalOperand(line[3].Value, lineNum) : (byte)0;
            var result = new byte[count];
            for (int i = 0; i < count; i++) result[i] = fill;
            return result;
        }

        // ===== Helper Methods =====

        /// <summary>
        /// Splits an instruction's operand tokens into a destination/source pair by locating
        /// the top-level comma separator.
        /// </summary>
        /// <param name="line">The full instruction token list (mnemonic first).</param>
        /// <returns>A tuple of (destination string, source string); either may be empty.</returns>
        private (string dst, string src) SplitOperands(List<Token> line)
        {
            var operands = SplitMultiOperands(line);
            if (operands.Count >= 2) return (operands[0], operands[1]);
            if (operands.Count == 1) return (operands[0], "");
            return ("", "");
        }

        /// <summary>
        /// Splits the operand tokens (everything after the mnemonic) into a list of strings
        /// separated by top-level commas, respecting nested parentheses.
        /// </summary>
        /// <param name="line">The full instruction token list (mnemonic first).</param>
        /// <returns>An ordered list of operand strings.</returns>
        private List<string> SplitMultiOperands(List<Token> line)
        {
            var parts = new List<string>();
            var current = new List<Token>();
            int depth = 0;
            foreach (var tok in line.Skip(1))
            {
                if (tok.Type == TokenType.LeftParen) { depth++; current.Add(tok); }
                else if (tok.Type == TokenType.RightParen) { depth--; current.Add(tok); }
                else if (tok.Type == TokenType.Comma && depth == 0)
                {
                    parts.Add(TokensToStr(current));
                    current.Clear();
                }
                else current.Add(tok);
            }
            if (current.Count > 0) parts.Add(TokensToStr(current));
            return parts;
        }

        /// <summary>Concatenates the <see cref="Token.Value"/> strings of a token list into a single string.</summary>
        /// <param name="tokens">The tokens to join.</param>
        /// <returns>The concatenated value string.</returns>
        private string TokensToStr(List<Token> tokens) => string.Join("", tokens.Select(t => t.Value));

        /// <summary>Returns <see langword="true"/> if <paramref name="s"/> is an indirect operand of the form <c>(…)</c>.</summary>
        /// <param name="s">The operand string to test.</param>
        private bool IsIndirect(string s) => s.Trim().StartsWith("(") && s.Trim().EndsWith(")");

        /// <summary>Returns <see langword="true"/> if the first token in <paramref name="toks"/> is a left parenthesis.</summary>
        /// <param name="toks">The token list to test.</param>
        private bool IsIndirect(List<Token> toks) => toks.Count > 0 && toks[0].Type == TokenType.LeftParen;

        /// <summary>
        /// Removes enclosing parentheses from an indirect operand string and trims whitespace.
        /// Returns the string unchanged if it is not parenthesised.
        /// </summary>
        /// <param name="s">The operand string, e.g. <c>"(HL)"</c>.</param>
        /// <returns>The inner string, e.g. <c>"HL"</c>.</returns>
        private string StripParens(string s)
        {
            s = s.Trim();
            if (s.StartsWith("(") && s.EndsWith(")")) return s.Substring(1, s.Length - 2).Trim();
            return s;
        }

        /// <summary>Returns the trimmed, upper-cased register/operand name from a raw operand string.</summary>
        /// <param name="s">The operand string.</param>
        private string GetReg(string s) => s.Trim().ToUpper();

        /// <summary>Returns <see langword="true"/> if <paramref name="r"/> names a valid Z80 8-bit register (A–L, IXH, IXL, IYH, IYL).</summary>
        /// <param name="r">The upper-cased register name.</param>
        private bool IsReg8(string r)
        {
            switch (r.ToUpper())
            {
                case "A": case "B": case "C": case "D": case "E": case "H": case "L":
                case "IXH": case "IXL": case "IYH": case "IYL": return true;
                default: return false;
            }
        }

        /// <summary>Returns <see langword="true"/> if <paramref name="r"/> names a valid Z80 16-bit register pair (BC, DE, HL, SP, AF, IX, IY).</summary>
        /// <param name="r">The upper-cased register name.</param>
        private bool IsReg16(string r)
        {
            switch (r.ToUpper())
            {
                case "BC": case "DE": case "HL": case "SP": case "AF":
                case "IX": case "IY": return true;
                default: return false;
            }
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="r"/> is a plain splittable 16-bit pair
        /// (BC, DE, HL, or AF) — valid as src/dst in the <c>LD rr,rr</c> Zilog pseudo-op.
        /// </summary>
        private bool IsReg16Plain(string r)
        {
            switch (r.ToUpper())
            {
                case "BC": case "DE": case "HL": case "AF": return true;
                default: return false;
            }
        }

        /// <summary>
        /// Splits a plain 16-bit register pair into its high and low 8-bit halves,
        /// used to expand the <c>LD rr,rr</c> pseudo-op into two 8-bit LD instructions.
        /// </summary>
        private (string hi, string lo) SplitReg16(string r)
        {
            switch (r.ToUpper())
            {
                case "BC": return ("B", "C");
                case "DE": return ("D", "E");
                case "HL": return ("H", "L");
                case "AF": return ("A", "F");
                default:   return ("H", "L");
            }
        }

        /// <summary>
        /// Returns the 3-bit register field code for an 8-bit register as used in Z80 opcodes.
        /// B=0, C=1, D=2, E=3, H=4, L=5, (HL)=6, A=7.
        /// </summary>
        /// <param name="r">The upper-cased register name.</param>
        private int Reg8Code(string r)
        {
            switch (r.ToUpper())
            {
                case "B": return 0; case "C": return 1; case "D": return 2;
                case "E": return 3; case "H": return 4; case "L": return 5;
                case "(HL)": case "M": return 6; case "A": return 7;
                case "IXH": return 4; case "IXL": return 5;
                case "IYH": return 4; case "IYL": return 5;
                default: return 7;
            }
        }

        /// <summary>
        /// Returns the 2-bit register-pair field code for a 16-bit register as used in most Z80 opcodes.
        /// BC=0, DE=1, HL=2, SP/AF=3.
        /// </summary>
        /// <param name="r">The upper-cased register name.</param>
        private int Reg16Code(string r)
        {
            switch (r.ToUpper())
            {
                case "BC": return 0; case "DE": return 1;
                case "HL": return 2; case "SP": return 3; case "AF": return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// Returns the 2-bit register-pair field code as used specifically by <c>PUSH</c>/<c>POP</c> opcodes,
        /// where AF (not SP) occupies slot 3.
        /// BC=0, DE=1, HL=2, AF/SP=3.
        /// </summary>
        /// <param name="r">The upper-cased register name.</param>
        private int Reg16PushCode(string r)
        {
            switch (r.ToUpper())
            {
                case "BC": return 0; case "DE": return 1;
                case "HL": return 2; case "AF": return 3; case "SP": return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// Maps an IX or IY register to its HL equivalent for opcode field calculation.
        /// All other registers are returned unchanged (upper-cased).
        /// </summary>
        /// <param name="r">The register name, e.g. <c>"IX"</c> → <c>"HL"</c>.</param>
        /// <returns>The base register name to use in opcode field calculations.</returns>
        private string BaseReg16(string r)
        {
            switch (r.ToUpper())
            {
                case "IX": return "HL";
                case "IY": return "HL";
                default: return r.ToUpper();
            }
        }

        /// <summary>
        /// Returns the DD or FD prefix byte array required before an instruction that uses IX or IY.
        /// Returns an empty array for any other register.
        /// </summary>
        /// <param name="r">The register name (e.g. <c>"IX"</c>, <c>"IYH"</c>).</param>
        /// <returns><c>{ 0xDD }</c> for IX variants, <c>{ 0xFD }</c> for IY variants, or <c>[]</c>.</returns>
        private byte[] IXIYPrefix(string r)
        {
            switch (r.ToUpper())
            {
                case "IX": case "IXH": case "IXL": return new byte[] { 0xDD };
                case "IY": case "IYH": case "IYL": return new byte[] { 0xFD };
                default: return new byte[0];
            }
        }

        /// <summary>
        /// Parses an IX/IY-indexed operand string (e.g. <c>"(IX+5)"</c> or <c>"(IY-3)"</c>)
        /// and returns the DD/FD prefix byte and the signed displacement value.
        /// Returns <c>(0, 0)</c> if the operand does not use IX or IY.
        /// </summary>
        /// <param name="s">The operand string to parse.</param>
        /// <returns>
        /// A tuple of (prefix byte, signed displacement integer).
        /// <c>prefix == 0</c> indicates the operand is not an IX/IY-indexed form.
        /// </returns>
        private (byte prefix, int disp) ParseIXIYDisp(string s)
        {
            s = s.Trim();
            string inner = IsIndirect(s) ? StripParens(s) : s;
            inner = inner.ToUpper();

            byte pre = 0;
            if (inner.StartsWith("IX")) pre = 0xDD;
            else if (inner.StartsWith("IY")) pre = 0xFD;
            else return (0, 0);

            // Parse +/- displacement
            int disp = 0;
            int signIdx = inner.IndexOfAny(new[] { '+', '-' }, 2);
            if (signIdx >= 0)
            {
                string dispStr = inner.Substring(signIdx);
                try { disp = (int)ParseNum(dispStr); } catch { }
            }
            return (pre, disp);
        }

        /// <summary>
        /// Maps a condition-code name to its 3-bit field value used in conditional jump/call/return opcodes.
        /// NZ=0, Z=1, NC=2, C=3, PO=4, PE=5, P=6, M=7.
        /// </summary>
        /// <param name="cc">The upper-cased condition-code name.</param>
        /// <returns>The 3-bit field value (0–7).</returns>
        private int CondCode(string cc)
        {
            switch (cc.ToUpper())
            {
                case "NZ": return 0; case "Z": return 1; case "NC": return 2; case "C": return 3;
                case "PO": return 4; case "PE": return 5; case "P": return 6; case "M": return 7;
                default: return 0;
            }
        }

        /// <summary>
        /// Evaluates a single operand string to an integer value.  Resolves labels from the
        /// symbol table, handles simple <c>label±offset</c> arithmetic, and parses numeric
        /// literals.  Returns 0 for unresolved forward references (patched later by
        /// <see cref="ApplyPatches"/>).
        /// </summary>
        /// <param name="s">The operand string to evaluate.</param>
        /// <param name="lineNum">Source line number for error context.</param>
        /// <returns>The evaluated integer value.</returns>
        private int EvalOperand(string s, int lineNum)
        {
            s = s.Trim();
            if (s == "$") return _pc;

            // Try label lookup
            if (_symbols.TryGetValue(s, out ushort addr)) return addr;

            // Simple arithmetic
            if (s.Contains("+") || s.Contains("-"))
            {
                try
                {
                    // Handle label+offset
                    int opIdx = -1;
                    for (int i = s.Length - 1; i >= 0; i--)
                        if (s[i] == '+' || s[i] == '-') { opIdx = i; break; }
                    if (opIdx > 0)
                    {
                        int left = EvalOperand(s.Substring(0, opIdx), lineNum);
                        int right = (int)ParseNum(s.Substring(opIdx + 1));
                        return s[opIdx] == '+' ? left + right : left - right;
                    }
                }
                catch { }
            }

            try { return (int)ParseNum(s); }
            catch
            {
                // Forward reference - return 0 placeholder
                return 0;
            }
        }

        /// <summary>
        /// Evaluates a single token as a numeric expression for use in <c>ORG</c> directives.
        /// Consumes the token at position <paramref name="i"/> and advances <paramref name="i"/> by one.
        /// </summary>
        /// <param name="tokens">The token list being processed.</param>
        /// <param name="i">The current token index; advanced in-place past the consumed token.</param>
        /// <param name="pc">The current program counter, used to resolve the <c>$</c> symbol.</param>
        /// <param name="lineNum">Source line number for error context (currently unused).</param>
        /// <returns>The evaluated value as a <see cref="long"/>.</returns>
        private long EvaluateExpr(List<Token> tokens, ref int i, ushort pc, int lineNum)
        {
            if (i >= tokens.Count) return 0;
            var tok = tokens[i];
            i++;
            if (tok.Type == TokenType.Number) return ParseNum(tok.Value);
            if (tok.Type == TokenType.Dollar) return pc;
            if (tok.Type == TokenType.Identifier)
            {
                if (_symbols.TryGetValue(tok.Value, out ushort v)) return v;
                return 0;
            }
            return 0;
        }

        /// <summary>
        /// Parses a numeric literal string to a <see cref="long"/> value.
        // ── Intel 8080 helper methods ────────────────────────────────────────────

        /// <summary>
        /// Returns the 2-bit register-pair code for an 8080 register pair operand.
        /// 8080 uses single-letter abbreviations: B=BC(0), D=DE(1), H=HL(2), SP/PSW=3.
        /// </summary>
        private int Reg16Code8080(string r)
        {
            switch (r.Trim().ToUpper())
            {
                case "B": case "BC":  return 0;
                case "D": case "DE":  return 1;
                case "H": case "HL":  return 2;
                case "SP": case "PSW": return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// Encodes an 8080 unconditional or conditional jump/call instruction with a 16-bit address.
        /// </summary>
        private byte[] Encode8080Jump(List<Token> line, int lineNum, byte opcode)
        {
            if (line.Count < 2) { _errors.Add(new AssemblerError(lineNum, $"{line[0].Value} requires address operand")); return new byte[0]; }
            int exI = 1;
            ushort nn = (ushort)EvaluateExpr(line, ref exI, _pc, lineNum);
            // Patch support: if the target is a forward label, record a patch
            if (line.Count > 1 && line[1].Type == TokenType.Identifier && !_symbols.ContainsKey(line[1].Value))
            {
                _patches.Add((_output.Count + 1, line[1].Value, lineNum, false));
                nn = 0;
            }
            return new byte[] { opcode, (byte)(nn & 0xFF), (byte)(nn >> 8) };
        }

        // ── Numeric literal parser ────────────────────────────────────────────────

        /// Supported formats: decimal (<c>255</c>), hex (<c>0xFF</c>, <c>$FF</c>, <c>0FFh</c>),
        /// and binary (<c>11001010b</c>).
        /// </summary>
        /// <param name="s">The numeric literal string to parse.</param>
        /// <returns>The parsed value.</returns>
        /// <remarks>Returns 0 for any input that cannot be parsed as a numeric literal.</remarks>
        private long ParseNum(string s)
        {
            s = s.Trim().Replace(" ", "");
            if (s.StartsWith("0x") || s.StartsWith("0X"))
                return Convert.ToInt64(s.Substring(2), 16);
            if (s.StartsWith("$") && s.Length > 1)
                return Convert.ToInt64(s.Substring(1), 16);
            if (s.EndsWith("h") || s.EndsWith("H"))
                return Convert.ToInt64(s.Substring(0, s.Length - 1), 16);
            if (s.EndsWith("b") || s.EndsWith("B") && s.Length > 1)
                try { return Convert.ToInt64(s.Substring(0, s.Length - 1), 2); } catch { }
            if (long.TryParse(s, out long v)) return v;
            return 0; // non-numeric token (symbol name etc.) — caller handles resolution
        }

        /// <summary>
        /// Resolves all forward-reference patches recorded during Pass 2.
        /// For each patch, looks up the label address in <see cref="_symbols"/> and writes
        /// either a signed 8-bit relative displacement (JR/DJNZ) or a 16-bit absolute
        /// address (JP/CALL/LD) into <see cref="_output"/> at the recorded offset.
        /// Reports an error for labels that remain undefined or for relative jumps that
        /// exceed the ±127-byte range.
        /// </summary>
        private void ApplyPatches()
        {
            foreach (var patch in _patches)
            {
                if (_symbols.TryGetValue(patch.label, out ushort addr))
                {
                    if (patch.isRelative)
                    {
                        int rel = addr - (patch.offset + _loadAddress + 2);
                        if (rel < -128 || rel > 127)
                            _errors.Add(new AssemblerError(patch.line, $"Relative jump to '{patch.label}' out of range"));
                        else if (patch.offset < _output.Count)
                            _output[patch.offset] = (byte)(sbyte)rel;
                    }
                    else
                    {
                        if (patch.offset + 1 < _output.Count)
                        {
                            _output[patch.offset] = (byte)(addr & 0xFF);
                            _output[patch.offset + 1] = (byte)(addr >> 8);
                        }
                    }
                }
                else
                    _errors.Add(new AssemblerError(patch.line, $"Undefined label '{patch.label}'"));
            }
        }

        /// <summary>
        /// Concatenates any number of byte arrays into a single new array.
        /// </summary>
        /// <param name="arrays">The arrays to concatenate in order.</param>
        /// <returns>A new byte array containing all input bytes.</returns>
        private byte[] Concat(params byte[][] arrays)
        {
            var result = new List<byte>();
            foreach (var arr in arrays) result.AddRange(arr);
            return result.ToArray();
        }

        /// <summary>
        /// Formats a single assembly listing line containing the hex address, the hex bytes
        /// of the encoded instruction, and the corresponding source line text.
        /// </summary>
        /// <param name="addr">The address at which the instruction is located.</param>
        /// <param name="bytes">The machine-code bytes produced for this instruction.</param>
        /// <param name="source">The full source text (used to extract the original source line).</param>
        /// <param name="lineNum">The 1-based source line number to extract.</param>
        /// <returns>A formatted listing line, e.g. <c>0100  3E 42        LD A,42h</c>.</returns>
        private string FormatListing(ushort addr, byte[] bytes, string source, int lineNum)
        {
            string hex = string.Join(" ", bytes.Select(b => b.ToString("X2")));
            string src = "";
            var lines = source.Split('\n');
            if (lineNum > 0 && lineNum <= lines.Length)
                src = lines[lineNum - 1].TrimEnd();
            return $"{addr:X4}  {hex,-12}  {src}";
        }

        // ===== Intel HEX Generator =====

        /// <summary>
        /// Converts a raw binary buffer to Intel HEX format.
        /// </summary>
        /// <remarks>
        /// Each data record contains up to <paramref name="recordSize"/> bytes.  Every record
        /// includes a two's-complement checksum.  The output is terminated by a standard
        /// end-of-file record (<c>:00000001FF</c>).
        /// </remarks>
        /// <param name="data">The machine-code bytes to encode.</param>
        /// <param name="loadAddr">The base load address written into the record address fields.</param>
        /// <param name="recordSize">Maximum bytes per data record (default 16).</param>
        /// <returns>The complete Intel HEX text, including the EOF record, with CRLF line endings.</returns>
        public static string GenerateIntelHex(byte[] data, ushort loadAddr, int recordSize = 16)
        {
            var sb = new StringBuilder();
            int offset = 0;

            while (offset < data.Length)
            {
                int count = Math.Min(recordSize, data.Length - offset);
                ushort address = (ushort)(loadAddr + offset);

                sb.Append(':');
                sb.Append(count.ToString("X2"));
                sb.Append(address.ToString("X4"));
                sb.Append("00");

                byte checksum = (byte)count;
                checksum += (byte)(address >> 8);
                checksum += (byte)(address & 0xFF);

                for (int i = 0; i < count; i++)
                {
                    sb.Append(data[offset + i].ToString("X2"));
                    checksum += data[offset + i];
                }

                checksum = (byte)(((~checksum) + 1) & 0xFF);
                sb.Append(checksum.ToString("X2"));
                sb.AppendLine();

                offset += count;
            }

            // End of file record
            sb.AppendLine(":00000001FF");
            return sb.ToString();
        }
    }
}
