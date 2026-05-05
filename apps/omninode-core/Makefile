CC ?= cc
CFLAGS ?= -std=c11 -Wall -Wextra -O2
LDLIBS ?=
EXEEXT :=
ifeq ($(OS),Windows_NT)
EXEEXT := .exe
LDLIBS += -lws2_32
endif

TARGET := omninode_core$(EXEEXT)
SRC := src/main.c

.PHONY: all clean run

all: $(TARGET)

$(TARGET): $(SRC)
	$(CC) $(CFLAGS) -o $@ $(SRC) $(LDLIBS)

run: $(TARGET)
	./$(TARGET)

clean:
	rm -f omninode_core omninode_core.exe
