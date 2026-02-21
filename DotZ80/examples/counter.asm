; =============================================================================
; counter.asm  —  Print digits 0-9 using a loop
; Demonstrates: loops, register arithmetic, DJNZ
; Assemble: dotz80 counter.asm -l -s
; =============================================================================

        ORG     0100h

BDOS    EQU     0005h
CONOUT  EQU     2               ; BDOS function: write single char to console

START:
        LD      B, 10           ; loop counter = 10 digits (0..9)
        LD      A, '0'          ; ASCII '0' = 48

LOOP:
        LD      E, A            ; E = character to print
        PUSH    AF              ; save A and flags
        PUSH    BC              ; save loop counter
        LD      C, CONOUT       ; BDOS function 2
        CALL    BDOS
        POP     BC              ; restore loop counter
        POP     AF              ; restore A
        INC     A               ; next digit
        DJNZ    LOOP            ; decrement B, jump if non-zero

        ; Print newline
        LD      E, 0Dh          ; CR
        LD      C, CONOUT
        CALL    BDOS
        LD      E, 0Ah          ; LF
        LD      C, CONOUT
        CALL    BDOS

        RET

        END     START
