namespace AdPerformance.CLI;

public static class ExitCodes
{
    public const int Success = 0;
    public const int UsageError = 1;
    public const int InputMissing = 2;
    public const int FatalIo = 3;
    public const int AllRowsInvalid = 4;
    public const int Unhandled = 5;
}
