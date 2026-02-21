![dotZ80](./assets/dotz80.png)

# DotZ80 — Z80 Assembler for CP/M

**.NET 10 · C# 13 · AGPL-3.0**

A two-pass Z80 assembler written in C#, targeting CP/M programs. Reads `.asm` or `.z80` source files and produces Intel HEX or raw binary output. Designed for use with CP/M emulators (RunCPM, MAME) and Z80 hardware programmers.

---

## Features

- **Two-pass assembler** — resolves forward label references correctly
- **Intel HEX output** — compatible with CP/M loaders, hardware programmers, and emulators
- **Raw binary output** — flat binary image starting at load address
- **Batch mode** — assemble multiple files in one invocation
- **Assembly listing** — address, hex bytes, and source line side by side
- **Symbol table** — all labels with their hex and decimal values
- **Assembler directives** — `ORG`, `EQU`, `DB`/`DEFB`/`DEFM`, `DW`/`DEFW`, `DS`/`DEFS`, `END`
- **ORG override** — set load address from the command line without editing source
- **Accepts `.asm` and `.z80`** source file extensions
- **Dot-prefixed processor directives** — `.Z80`, `.Z180`, etc. are silently ignored
- **Coloured ANSI console output** — with `--no-color` fallback

---

## Build Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

---

## Build & Run

```powershell
# Build (debug)
cd Z80AsmCLI
dotnet build

# Run directly from source
dotnet run --project Z80AsmCLI -- examples/hello.asm

# Publish as single self-contained EXE (Windows x64)
cd Z80AsmCLI
dotnet publish -c Release
# Output: Z80AsmCLI\bin\Release\net10.0\win-x64\publish\dotz80.exe
```

After publishing, copy `dotz80.exe` anywhere on your `PATH` and use it globally.

---

## Usage

```
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
```

---

## Examples

```powershell
# Basic assembly — produces hello.hex
dotz80 examples\hello.asm

# Assemble a .z80 file
dotz80 examples\hello.z80

# Assembly with listing and symbol table printed to console
dotz80 examples\counter.asm -l -s

# Custom output path + write listing to file
dotz80 examples\strrev.asm -o out\strrev.hex --list-file out\strrev.lst

# Raw binary output
dotz80 mycode.asm --binary -o mycode.bin

# Override load address (e.g. ROM at 0x8000)
dotz80 mycode.asm --org 0x8000

# Pipe Intel HEX to another tool
dotz80 mycode.asm --stdout | xxd

# Batch assemble multiple files (each gets its own .hex)
dotz80 examples\hello.asm examples\counter.asm examples\strrev.asm

# Batch assemble .z80 files
dotz80 a.z80 b.z80 c.z80
```

---

## Supported Z80 Instructions

| Group | Instructions |
|---|---|
| **Load / Move** | `LD` `LDD` `LDI` `LDDR` `LDIR` `PUSH` `POP` |
| **Arithmetic** | `ADD` `ADC` `SUB` `SBC` `INC` `DEC` `NEG` `DAA` `CPL` |
| **Logic** | `AND` `OR` `XOR` `CP` `SCF` `CCF` |
| **Rotate / Shift** | `RLCA` `RRCA` `RLA` `RRA` `RL` `RR` `RLC` `RRC` `SLA` `SRA` `SRL` `SLL` |
| **Bit Operations** | `BIT` `SET` `RES` |
| **Jump / Call / Return** | `JP` `JR` `CALL` `RET` `RETI` `RETN` `DJNZ` `RST` |
| **I/O** | `IN` `OUT` `INI` `IND` `INIR` `INDR` `OUTI` `OUTD` `OTIR` `OTDR` |
| **Control** | `DI` `EI` `HALT` `NOP` `EX` `EXX` `IM` |

### Assembler Directives

| Directive | Description |
|---|---|
| `ORG <addr>` | Set program counter / load address |
| `EQU <expr>` | Define a named constant (`LABEL EQU value` or `LABEL: EQU value`) |
| `DB` / `DEFB` / `DEFM` | Define byte(s) or string data |
| `DW` / `DEFW` | Define 16-bit word(s) |
| `DS` / `DEFS` | Reserve (define space) N bytes |
| `END` | End of source file |

---

## Number Formats

| Style | Example | Notes |
|---|---|---|
| Decimal | `255` | Standard base-10 |
| Hex — H suffix | `0FFh` | Trailing `h` or `H` |
| Hex — 0x prefix | `0xFF` | C-style |
| Hex — $ prefix | `$FF` | Assembly-style |
| Binary | `10110b` | Trailing `b` |
| Current PC | `$` | Dollar sign alone = program counter |

---

## Project Structure

```
Z80AsmCLI.sln
Z80AsmCLI/
  Z80AsmCLI.csproj          .NET 10 console project (namespace: DotZ80)
  Program.cs                Top-level entry point
  CliParser.cs              CLI argument parsing (CliOptions record)
  ConsoleWriter.cs          Coloured ANSI console output helper
  AssemblerRunner.cs        File I/O, reporting, listing/symbol output
  Assembler/
    Lexer.cs                Tokenizer — classifies Z80 mnemonics, registers, literals
    Z80AssemblerEngine.cs   Two-pass assembler + Intel HEX generator
examples/
  hello.asm                 CP/M Hello World
  counter.asm               Loop with DJNZ, digit printing
  strrev.asm                String reversal subroutine
```

---

## C# Features Used

| Feature | Where |
|---|---|
| Top-level statements | `Program.cs` |
| Records (`record`, `sealed record`) | `CliOptions` in `CliParser.cs` |
| `init`-only setters | `CliOptions` properties |
| `IReadOnlyList<T>` with collection expression `[]` | `CliOptions.BatchFiles` |
| Primary constructors | `AssemblerRunner(ConsoleWriter, CliOptions)` |
| `is` pattern matching | `CliParser.Parse` |
| Nullable reference types | Throughout |
| String interpolation | Throughout |
| Global usings (`ImplicitUsings`) | Project-wide |
| Raw string literals (`"""`) | `HelpText` in `CliParser.cs` |

---

## License

Copyright (C) 2026 Menno Bolt

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU Affero General Public License** as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

See the [LICENSE](LICENSE) file for the full license text, or visit <https://www.gnu.org/licenses/agpl-3.0.html>.
