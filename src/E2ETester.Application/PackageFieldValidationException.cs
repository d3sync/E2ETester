namespace E2ETester.Application;

// Only schema-owned paths and fixed guidance belong here; never include supplied values.
public sealed class PackageFieldValidationException(string path, string guidance) : FormatException($"{path}: {guidance}")
{
    public string FieldPath { get; } = path;
}
