namespace AnalyseTool.App.Common.Dispatch
{
    internal enum WebMessageTypeEnum
    {
        Request,
        Response,

        /// <summary>Inbound: abandon an in-flight Request by its Id. Only the inbound types are named
        /// here — the outbound "Event" and "Progress" are literals at their send sites.</summary>
        Cancel,

        /// <summary>Inbound: "are you receiving?". Answered with an outbound "Pong" straight from the
        /// receive handler, on the UI thread, before any queue — so the answer's absence measures exactly
        /// the thing the page cannot otherwise see: that Revit's thread is held and nothing it posts is
        /// being received (#102).</summary>
        Ping
    }
}
