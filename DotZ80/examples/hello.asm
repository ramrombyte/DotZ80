; =============================================================================
; hello.asm  —  Hello World for CP/M
; Assemble: dotz80 hello.asm
; =============================================================================

        ORG     0100h           ; CP/M programs load at 0x0100

BDOS    EQU     0005h           ; CP/M BDOS call vector
PRINT   EQU     9               ; BDOS function: print $ terminated string

START:
        LD      C, PRINT        ; C = BDOS function 9
        LD      DE, MSG         ; DE = address of message
        CALL    BDOS            ; call CP/M BDOS
        RET                     ; return to CCP

MSG:
        DEFM    'Hello, Z80 World!'
        DB      0Dh             ; CR
        DB      0Ah             ; LF
        DB      '$'             ; CP/M string terminator

        END     START
