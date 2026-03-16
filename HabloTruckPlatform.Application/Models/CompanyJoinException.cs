namespace HabloTruckPlatform.Application.Models;

public sealed class CompanyJoinException : Exception
{
    public CompanyJoinException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
