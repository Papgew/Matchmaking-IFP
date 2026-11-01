using System.Runtime.InteropServices;

namespace Matchmaking.Core;

public static class NativeBonus
{
    // C int maps to C# int; cdecl; returns clamp((kills - deaths) * weight, -30, 30).
    [DllImport("matchbonus", EntryPoint = "mm_bonus", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CalculateNative(int kills, int deaths, int weight);

    public static Result<int> Compute(int kills, int deaths, int weight) =>
        ComputeWith(kills, deaths, weight, CalculateNative);

    // Injectable boundary permits deterministic error-path tests without faking the real-call test.
    public static Result<int> ComputeWith(
        int kills, int deaths, int weight, Func<int, int, int, int> invoke)
    {
        if (kills < 0 || deaths < 0 || weight < 0 || weight > 10)
            return Result<int>.Fail("INVALID_NATIVE_INPUT", "Kills/deaths must be nonnegative and weight must be 0..10.");
        try
        {
            return Result<int>.Ok(invoke(kills, deaths, weight));
        }
        catch (DllNotFoundException ex)
        {
            return Result<int>.Fail("NATIVE_LIBRARY_MISSING", ex.Message);
        }
        catch (EntryPointNotFoundException ex)
        {
            return Result<int>.Fail("NATIVE_ENTRY_MISSING", ex.Message);
        }
        catch (BadImageFormatException ex)
        {
            return Result<int>.Fail("NATIVE_ARCHITECTURE_MISMATCH", ex.Message);
        }
    }
}
