namespace Squash;

public readonly record struct PeSection(
    int RawPointer,
    int RawSize,
    int VirtualAddress,
    int VirtualSize);