namespace BleLibrary.Exceptions
{
    public sealed class BleServiceException : Exception
    {
        public BleServiceException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
