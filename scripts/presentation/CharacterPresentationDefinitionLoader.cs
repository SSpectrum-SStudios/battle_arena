#nullable enable

using System.Text.Json;
using System.IO;
using Godot;

namespace BattleArena.Presentation;

public static class CharacterPresentationDefinitionLoader
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static CharacterPresentationDefinition Load(string resourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);

        if (!resourcePath.StartsWith("res://", StringComparison.Ordinal))
        {
            throw new ArgumentException("Presentation definitions must use a res:// path.", nameof(resourcePath));
        }

        if (!Godot.FileAccess.FileExists(resourcePath))
        {
            throw new FileNotFoundException("Character presentation definition was not found.", resourcePath);
        }

        var json = Godot.FileAccess.GetFileAsString(resourcePath);
        CharacterPresentationDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<CharacterPresentationDefinition>(json, SerializerOptions)
                ?? throw new InvalidDataException("The character presentation definition is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Character presentation definition '{resourcePath}' is invalid JSON.", exception);
        }

        Validate(definition, resourcePath);
        return definition;
    }

    private static void Validate(CharacterPresentationDefinition definition, string resourcePath)
    {
        if (definition.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Character presentation '{resourcePath}' uses schema {definition.SchemaVersion}; " +
                $"schema {SupportedSchemaVersion} is required.");
        }

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new InvalidDataException($"Character presentation '{resourcePath}' requires an id.");
        }

        if (string.IsNullOrWhiteSpace(definition.ModelScenePath) ||
            !definition.ModelScenePath.StartsWith("res://", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Character presentation '{definition.Id}' requires a res:// modelScenePath.");
        }

        if (!float.IsFinite(definition.ModelScale) || definition.ModelScale <= 0f)
        {
            throw new InvalidDataException($"Character presentation '{definition.Id}' requires a positive finite modelScale.");
        }

        foreach (var (semanticId, binding) in definition.Animations)
        {
            if (string.IsNullOrWhiteSpace(semanticId) || string.IsNullOrWhiteSpace(binding.Clip))
            {
                throw new InvalidDataException(
                    $"Character presentation '{definition.Id}' contains an incomplete animation binding.");
            }

            if (!float.IsFinite(binding.BlendSeconds) || binding.BlendSeconds < 0f)
            {
                throw new InvalidDataException(
                    $"Animation '{semanticId}' on character presentation '{definition.Id}' has an invalid blend duration.");
            }

            if (binding.ReferenceSpeed is { } referenceSpeed &&
                (!float.IsFinite(referenceSpeed) || referenceSpeed <= 0f))
            {
                throw new InvalidDataException(
                    $"Animation '{semanticId}' on character presentation '{definition.Id}' has an invalid reference speed.");
            }

        }
    }
}
