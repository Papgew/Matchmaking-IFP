#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p linux-x64
gcc -std=c11 -O2 -Wall -Wextra -fPIC -shared bonus.c -o linux-x64/libmatchbonus.so
echo "Built native/linux-x64/libmatchbonus.so"
