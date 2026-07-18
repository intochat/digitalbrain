namespace TripRadar.Server.Comms.Core.Contracts.Factories;

public interface IFormatterFactory
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="contentType"></param>
    /// <returns></returns>
    IFormatter GetFormatter(string contentType);
}