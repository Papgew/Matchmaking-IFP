using System.Runtime.InteropServices;

namespace Matchmaking.Core;

public static class NativeBonus
{
    // C int <-> C# int; cdecl; exported by native/bonus.c.
    [DllImport("matchbonus", EntryPoint = "mm_bonus", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CalculateNative(int kills, int deaths, int weight);

    public static Result<int> Compute(int kills, int deaths, int weight) =>
        ComputeWith(kills, deaths, weight, CalculateNative);

    // Injectable boundary makes failure-path tests deterministic, while Compute tests real P/Invoke.
    public static Result<int> ComputeWith(
        int kills, int deaths, int weight, Func<int, int, int, int> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        if (kills < 0 || deaths < 0 || weight is < 0 or > 10)
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
