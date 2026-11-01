#include "bonus.h"

// Contract: kills/deaths >= 0, weight 0..10; caller validates inputs.
// Compute in 64-bit arithmetic to avoid signed int overflow.
int mm_bonus(int kills, int deaths, int weight)
{
    long long value = ((long long)kills - (long long)deaths) * (long long)weight;
    if (value > 30) return 30;
    if (value < -30) return -30;
    return (int)value;
}
