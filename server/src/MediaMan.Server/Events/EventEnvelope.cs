namespace MediaMan.Server.Events;

/// <summary>One real-time update sent over SSE; the contract's <c>EventEnvelope</c>.</summary>
public sealed record EventEnvelope(string Id, string Type, DateTimeOffset At, object Data);
