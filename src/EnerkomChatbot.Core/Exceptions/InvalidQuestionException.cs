namespace EnerkomChatbot.Core.Exceptions;

/// <summary>Dotaz neprošel validací (prázdný / příliš dlouhý) → API mapuje na HTTP 400.</summary>
public sealed class InvalidQuestionException(string message) : Exception(message);
