#include "bonus.h"

// The C# adapter validates kills/deaths >= 0 and weight in [0,10].
// Widen before subtract/multiply to prevent signed 32-bit overflow.
int mm_bonus(int kills, int deaths, int weight)
{
    long long value = ((long long)kills - (long long)deaths) * (long long)weight;
    if (value > 30) return 30;
    if (value < -30) return -30;
    return (int)value;
}
