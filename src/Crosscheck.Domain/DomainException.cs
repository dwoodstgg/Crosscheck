namespace Crosscheck.Domain;

/// <summary>A domain rule was violated (e.g. removing the last Admin). Message is safe to show to users.</summary>
public class DomainException(string message) : Exception(message);
