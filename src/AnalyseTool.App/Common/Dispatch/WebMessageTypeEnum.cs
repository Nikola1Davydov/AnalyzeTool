namespace AnalyseTool.App.Common.Dispatch
{
    internal enum WebMessageTypeEnum
    {
        Request,
        Response,

        /// <summary>Inbound: abandon an in-flight Request by its Id. Only the inbound types are named
        /// here — the outbound "Event" and "Progress" are literals at their send sites.</summary>
        Cancel
    }
}
