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

namespace DotZ80.Assembler
{
    /// <summary>
    /// Categorises every lexical unit that the Z80 assembler tokenizer can produce.
    /// </summary>
    public enum TokenType
    {
        /// <summary>A user-defined symbol followed by a colon (e.g. <c>LOOP:</c>).</summary>
        Label,

        /// <summary>A Z80 instruction mnemonic or assembler directive (e.g. <c>LD</c>, <c>ORG</c>).</summary>
        Mnemonic,

        /// <summary>A CPU register name or condition code (e.g. <c>A</c>, <c>HL</c>, <c>NZ</c>).</summary>
        Register,

        /// <summary>An integer literal in decimal, hexadecimal, or binary notation.</summary>
        Number,

        /// <summary>A quoted string literal delimited by single or double quotes.</summary>
        String,

        /// <summary>An operand separator (<c>,</c>).</summary>
        Comma,

        /// <summary>A label terminator (<c>:</c>).</summary>
        Colon,

        /// <summary>An opening parenthesis used for indirect addressing (<c>(</c>).</summary>
        LeftParen,

        /// <summary>A closing parenthesis used for indirect addressing (<c>)</c>).</summary>
        RightParen,

        /// <summary>An addition operator (<c>+</c>), also used in index-register displacement expressions.</summary>
        Plus,

        /// <summary>A subtraction operator (<c>-</c>).</summary>
        Minus,

        /// <summary>A multiplication operator (<c>*</c>).</summary>
        Multiply,

        /// <summary>A division operator (<c>/</c>).</summary>
        Divide,

        /// <summary>
        /// A bare dollar sign (<c>$</c>) representing the current program counter value,
        /// or a <c>$HEX</c> hex-literal prefix when followed by hex digits.
        /// </summary>
        Dollar,

        /// <summary>An unrecognised word that is not a mnemonic or register — typically a label reference.</summary>
        Identifier,

        /// <summary>A logical line terminator inserted after each source line.</summary>
        NewLine,

        /// <summary>The sentinel token appended at the end of the token stream.</summary>
        EOF,

        /// <summary>An equals sign (<c>=</c>), used in <c>DEFC</c> constant definitions.</summary>
        Equals,

        /// <summary>Any character that does not match any other token category.</summary>
        Unknown,
    }

    /// <summary>Represents a single lexical token produced by the <see cref="Lexer"/>.</summary>
    public class Token
    {
        /// <summary>The category of this token.</summary>
        public TokenType Type { get; set; }

        /// <summary>The raw text of this token as it appears in the source (normalised to upper-case for mnemonics and registers).</summary>
        public string Value { get; set; }

        /// <summary>The 1-based source line number on which this token appears.</summary>
        public int Line { get; set; }

        /// <summary>Initialises a new token with the specified type, value, and source line.</summary>
        /// <param name="type">The token category.</param>
        /// <param name="value">The raw text of the token.</param>
        /// <param name="line">The 1-based source line number.</param>
        public Token(TokenType type, string value, int line)
        {
            Type = type;
            Value = value;
            Line = line;
        }

        /// <summary>Returns a diagnostic representation of the token, e.g. <c>[Mnemonic: 'LD' @L5]</c>.</summary>
        public override string ToString() => $"[{Type}: '{Value}' @L{Line}]";
    }

    /// <summary>
    /// Tokenizes Z80 assembly source text into a flat list of <see cref="Token"/> objects
    /// ready for consumption by <see cref="Z80AssemblerEngine"/>.
    /// </summary>
    /// <remarks>
    /// The lexer performs a single linear pass over each source line.  Comments (introduced
    /// by <c>;</c>) are stripped before tokenization.  Number literals are normalised to a
    /// consistent internal representation: decimal stays as-is, hex uses the <c>0x</c> prefix.
    /// </remarks>
    public class Lexer
    {
        /// <summary>
        /// Case-insensitive set of all recognised Z80 instruction mnemonics and assembler directives.
        /// Identifiers found in this set are emitted as <see cref="TokenType.Mnemonic"/> tokens.
        /// </summary>
        private static readonly HashSet<string> Mnemonics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Z80 instructions ────────────────────────────────────────────────────
            "LD","LDD","LDI","LDDR","LDIR","PUSH","POP",
            "ADD","ADC","SUB","SBC","AND","OR","XOR","CP","INC","DEC","NEG","CPL","DAA","RLCA","RRCA","RLA","RRA",
            "RL","RR","RLC","RRC","SLA","SRA","SRL","SLL",
            "BIT","SET","RES",
            "JP","JR","CALL","RET","RETI","RETN","DJNZ","RST",
            "IN","OUT","INI","IND","INIR","INDR","OUTI","OUTD","OTIR","OTDR",
            "EX","EXX","DI","EI","HALT","NOP","SCF","CCF","IM",
            // ── Assembler directives ────────────────────────────────────────────────
            "DB","DW","DS","ORG","EQU","DEFB","DEFW","DEFS","DEFM","END","INCLUDE",
            "DEFC","PUBLIC","EXTERN","GLOBAL","MODULE","SECTION",
            // ── Intel 8080 mnemonics (tokenised but not encoded — emits error) ─────
            "MOV","MVI","LXI","LDA","STA","LHLD","SHLD","LDAX","STAX",
            "ADD","ADI","ACI","SUB","SBI","SUI","SBB","ANA","ORA","XRA","CMP",
            "ANI","ORI","XRI","CPI","ADC",
            "INR","DCR","INX","DCX","DAD",
            "RLC","RRC","RAL","RAR",
            "JMP","JC","JNC","JZ","JNZ","JP","JM","JPE","JPO",
            "CALL","CC","CNC","CZ","CNZ","CP","CM","CPE","CPO",
            "RET","RC","RNC","RZ","RNZ","RP","RM","RPE","RPO",
            "RST","PCHL","SPHL","XTHL","XCHG",
            "PUSH","POP",
            "IN","OUT","EI","DI","HLT","NOP","STC","CMC","CMA","DAA",
            "TITLE","PAGE","EJECT","NAME","STKLN","MACLIB",
            "IF","ELSE","ENDIF","SET",
        };

        /// <summary>
        /// Case-insensitive set of all recognised Z80 register names and condition codes.
        /// Identifiers found in this set are emitted as <see cref="TokenType.Register"/> tokens.
        /// </summary>
        private static readonly HashSet<string> Registers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "A","B","C","D","E","H","L","F",
            "AF","BC","DE","HL","SP","PC","IX","IY",
            "IXH","IXL","IYH","IYL","I","R",
            "AF'","NZ","Z","NC","PO","PE","P","M"
        };

        /// <summary>
        /// Tokenizes the entire assembly source text and returns a flat list of tokens.
        /// </summary>
        /// <remarks>
        /// Processing rules:
        /// <list type="bullet">
        ///   <item>Each source line is split on <c>\n</c>; a <see cref="TokenType.NewLine"/> token is appended after every line.</item>
        ///   <item>Everything from the first <c>;</c> to end-of-line is treated as a comment and discarded.</item>
        ///   <item>String literals (<c>"…"</c> or <c>'…'</c>) are captured as a single <see cref="TokenType.String"/> token.</item>
        ///   <item>Hex prefixes: <c>$FEED</c>, <c>0xFEED</c>, or <c>FEEDh</c> — all normalised to <c>0xFEED</c>.</item>
        ///   <item>A bare <c>$</c> (not followed by a hex digit) becomes a <see cref="TokenType.Dollar"/> token (current PC).</item>
        ///   <item>Identifiers are matched against <see cref="Mnemonics"/> and <see cref="Registers"/> and classified accordingly.</item>
        /// </list>
        /// A single <see cref="TokenType.EOF"/> token is appended at the very end of the stream.
        /// </remarks>
        /// <param name="source">The complete Z80 assembly source text to tokenize.</param>
        /// <returns>An ordered list of tokens representing the entire source file.</returns>
        public List<Token> Tokenize(string source)
        {
            var tokens = new List<Token>();
            var lines = source.Split('\n');

            for (int lineNum = 0; lineNum < lines.Length; lineNum++)
            {
                var line = lines[lineNum];
                // Strip comments
                int commentIdx = line.IndexOf(';');
                if (commentIdx >= 0) line = line.Substring(0, commentIdx);

                int i = 0;
                while (i < line.Length)
                {
                    char c = line[i];

                    if (char.IsWhiteSpace(c)) { i++; continue; }

                    // String literal
                    if (c == '"' || c == '\'')
                    {
                        char quote = c;
                        int start = i + 1;
                        i++;
                        while (i < line.Length && line[i] != quote) i++;
                        string str = line.Substring(start, i - start);
                        tokens.Add(new Token(TokenType.String, str, lineNum + 1));
                        i++;
                        continue;
                    }

                    // Hex number 0x... or $...
                    if (c == '$' && i + 1 < line.Length && IsHexChar(line[i + 1]))
                    {
                        int start = i + 1;
                        i++;
                        while (i < line.Length && IsHexChar(line[i])) i++;
                        tokens.Add(new Token(TokenType.Number, "0x" + line.Substring(start, i - start), lineNum + 1));
                        continue;
                    }

                    if (c == '$')
                    {
                        tokens.Add(new Token(TokenType.Dollar, "$", lineNum + 1));
                        i++;
                        continue;
                    }

                    if (char.IsDigit(c) || (c == '0' && i + 1 < line.Length && (line[i + 1] == 'x' || line[i + 1] == 'X')))
                    {
                        int start = i;
                        if (c == '0' && i + 1 < line.Length && (line[i + 1] == 'x' || line[i + 1] == 'X'))
                        {
                            i += 2;
                            while (i < line.Length && IsHexChar(line[i])) i++;
                        }
                        else
                        {
                            // Allow $ as a visual digit-group separator (8080 style: 1111$1110B)
                            while (i < line.Length && (char.IsDigit(line[i]) || IsHexChar(line[i]) || line[i] == '$')) i++;
                            // Check for B suffix (binary)
                            if (i < line.Length && (line[i] == 'b' || line[i] == 'B') &&
                                !char.IsLetterOrDigit(i + 1 < line.Length ? line[i + 1] : ' '))
                            {
                                // Strip embedded $ separators, then parse as binary
                                string binVal = line.Substring(start, i - start).Replace("$", "");
                                tokens.Add(new Token(TokenType.Number, binVal + "b", lineNum + 1));
                                i++;
                                continue;
                            }
                            // Check for H suffix (hex)
                            if (i < line.Length && (line[i] == 'h' || line[i] == 'H') &&
                                !char.IsLetterOrDigit(i + 1 < line.Length ? line[i + 1] : ' '))
                            {
                                string hexVal = line.Substring(start, i - start).Replace("$", "");
                                tokens.Add(new Token(TokenType.Number, "0x" + hexVal, lineNum + 1));
                                i++;
                                continue;
                            }
                        }
                        tokens.Add(new Token(TokenType.Number, line.Substring(start, i - start), lineNum + 1));
                        continue;
                    }

                    // Dot-prefixed directive: .Z80, .Z180, .8080, etc.
                    if (c == '.' && i + 1 < line.Length && (char.IsLetterOrDigit(line[i + 1]) || line[i + 1] == '_'))
                    {
                        int start = i; // include the dot
                        i++;
                        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                        string word = line.Substring(start, i - start);
                        // Emit as Mnemonic so the engine can handle (ignore) it
                        tokens.Add(new Token(TokenType.Mnemonic, word.ToUpper(), lineNum + 1));
                        continue;
                    }

                    if (char.IsLetter(c) || c == '_')
                    {
                        int start = i;
                        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_' || line[i] == '\'' || line[i] == '$')) i++;
                        string word = line.Substring(start, i - start);
                        // Strip embedded '$' separators for lookup and storage:
                        // 8080 source uses '$' as a word-separator in identifiers (e.g. set$alloc$bit == setallocbit)
                        string normalized = word.Replace("$", "");

                        if (Registers.Contains(normalized))
                            tokens.Add(new Token(TokenType.Register, normalized.ToUpper(), lineNum + 1));
                        else if (Mnemonics.Contains(normalized))
                            tokens.Add(new Token(TokenType.Mnemonic, normalized.ToUpper(), lineNum + 1));
                        else
                            tokens.Add(new Token(TokenType.Identifier, normalized, lineNum + 1));
                        continue;
                    }

                    switch (c)
                    {
                        case ':': tokens.Add(new Token(TokenType.Colon, ":", lineNum + 1)); break;
                        case ',': tokens.Add(new Token(TokenType.Comma, ",", lineNum + 1)); break;
                        case '(': tokens.Add(new Token(TokenType.LeftParen, "(", lineNum + 1)); break;
                        case ')': tokens.Add(new Token(TokenType.RightParen, ")", lineNum + 1)); break;
                        case '+': tokens.Add(new Token(TokenType.Plus, "+", lineNum + 1)); break;
                        case '-': tokens.Add(new Token(TokenType.Minus, "-", lineNum + 1)); break;
                        case '*': tokens.Add(new Token(TokenType.Multiply, "*", lineNum + 1)); break;
                        case '/': tokens.Add(new Token(TokenType.Divide, "/", lineNum + 1)); break;
                        case '=': tokens.Add(new Token(TokenType.Equals, "=", lineNum + 1)); break;
                        default: tokens.Add(new Token(TokenType.Unknown, c.ToString(), lineNum + 1)); break;
                    }
                    i++;
                }

                tokens.Add(new Token(TokenType.NewLine, "\n", lineNum + 1));
            }

            tokens.Add(new Token(TokenType.EOF, "", lines.Length));
            return tokens;
        }

        /// <summary>Returns <see langword="true"/> if <paramref name="c"/> is a valid hexadecimal digit (<c>0–9</c>, <c>a–f</c>, <c>A–F</c>).</summary>
        /// <param name="c">The character to test.</param>
        private bool IsHexChar(char c) =>
            char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
