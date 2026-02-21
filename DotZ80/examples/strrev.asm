; =============================================================================
; strrev.asm  —  Reverse a string in-place, then print it
; Demonstrates: subroutines, stack usage, (HL) addressing
; Assemble: dotz80 strrev.asm -l -s
; =============================================================================

        ORG     0100h

BDOS    EQU     0005h
PRINT   EQU     9

; ─────────────────────────────────────────────────────────────────────────────
START:
        LD      HL, TEXT        ; point HL to string
        CALL    STRLEN          ; BC = length (excluding $)
        CALL    STRREV          ; reverse the string in-place
        LD      C, PRINT
        LD      DE, TEXT
        CALL    BDOS
        RET

; ─────────────────────────────────────────────────────────────────────────────
; STRLEN  — count bytes until '$', result in BC
; Entry:  HL = start of string
; Exit:   BC = length
STRLEN:
        LD      BC, 0
.LOOP:
        LD      A, (HL)
        CP      '$'
        RET     Z               ; return when '$' found
        INC     HL
        INC     BC
        JR      .LOOP

; ─────────────────────────────────────────────────────────────────────────────
; STRREV  — reverse string in place
; Entry:  HL = start address, BC = length
; Clobbers: DE, HL, A
STRREV:
        LD      D, H            ; DE = HL (start)
        LD      E, L
        ADD     HL, BC          ; HL = end + 1
        DEC     HL              ; HL = last char
        SRL     B               ; BC = BC / 2  (number of swaps)
        RR      C
        LD      A, B
        OR      C
        RET     Z               ; nothing to do for length 0 or 1
.SWAP:
        LD      A, (DE)         ; load from start
        LD      B, (HL)         ; load from end  (temp in B)
        LD      (HL), A         ; store start char at end
        LD      A, B
        LD      (DE), A         ; store end char at start
        INC     DE
        DEC     HL
        DEC     C               ; decrement swap count
        JR      NZ, .SWAP
        RET

; ─────────────────────────────────────────────────────────────────────────────
TEXT:
        DEFM    'Hello, Z80!'
        DB      0Dh, 0Ah, '$'

        END     START
