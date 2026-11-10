namespace SecureProxy.Domain.Enums
{
    public enum RequestOutcome
    {
        Forwarded,           // sent to the destination and it responded (any status)
        Rejected,            // stopped by a validation or policy check; never forwarded
        UpstreamTimeout,     // forwarded, destination did not answer in time (504)
        UpstreamUnavailable, // forwarded, could not connect (502)
        Cancelled,           // the caller cancelled the request
        Failed               // unexpected server error (500)
    }
}
