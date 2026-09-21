// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;

namespace MS.Internal.AppModel
{
    /// <summary>
    /// Reconstructs an ICO stream from the RT_GROUP_ICON/RT_ICON resources emitted
    /// into a managed PE when a project specifies ApplicationIcon.
    /// </summary>
    internal static class PortableExecutableIconReader
    {
        private const int IconResourceType = 3;
        private const int GroupIconResourceType = 14;
        private const uint DirectoryFlag = 0x80000000;
        private const uint OffsetMask = 0x7fffffff;
        private const int ResourceDirectoryHeaderSize = 16;
        private const int ResourceDirectoryEntrySize = 8;
        private const int ResourceDataEntrySize = 16;
        private const int GroupIconDirectoryHeaderSize = 6;
        private const int GroupIconDirectoryEntrySize = 14;
        private const int IconDirectoryEntrySize = 16;
        private const int MaximumIconCount = 1024;
        private const int MaximumIconBytes = 64 * 1024 * 1024;

        internal static bool TryReadApplicationIcon(string filePath, out byte[] iconBytes)
        {
            iconBytes = null;
            if (string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            try
            {
                using FileStream stream = File.OpenRead(filePath);
                using PEReader peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
                return TryReadApplicationIcon(peReader, out iconBytes);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                BadImageFormatException or
                ArgumentException or
                InvalidOperationException or
                OverflowException)
            {
                iconBytes = null;
                return false;
            }
        }

        private static bool TryReadApplicationIcon(PEReader peReader, out byte[] iconBytes)
        {
            iconBytes = null;
            DirectoryEntry resourceDirectory = peReader.PEHeaders.PEHeader?.ResourceTableDirectory ?? default;
            if (resourceDirectory.RelativeVirtualAddress <= 0 || resourceDirectory.Size <= 0)
            {
                return false;
            }

            byte[] resources = peReader
                .GetSectionData(resourceDirectory.RelativeVirtualAddress)
                .GetContent()
                .ToArray();
            if (resources.Length < ResourceDirectoryHeaderSize ||
                !TryFindIdDirectory(resources, 0, GroupIconResourceType, out int groupTypeDirectory) ||
                !TryFindFirstDirectory(resources, groupTypeDirectory, out int groupDirectory, out _) ||
                !TryFindFirstDataEntry(resources, groupDirectory, out int groupDataEntry, out int languageId) ||
                !TryReadResourceData(peReader, resources, groupDataEntry, out byte[] groupData) ||
                !TryReadGroupEntries(groupData, out GroupIconEntry[] groupEntries) ||
                !TryFindIdDirectory(resources, 0, IconResourceType, out int iconTypeDirectory))
            {
                return false;
            }

            List<IconImage> images = new List<IconImage>(groupEntries.Length);
            foreach (GroupIconEntry entry in groupEntries)
            {
                if (!TryFindIdDirectory(resources, iconTypeDirectory, entry.ResourceId, out int imageDirectory) ||
                    !TryFindDataEntry(resources, imageDirectory, languageId, out int imageDataEntry) ||
                    !TryReadResourceData(peReader, resources, imageDataEntry, out byte[] imageBytes) ||
                    imageBytes.Length == 0)
                {
                    continue;
                }

                images.Add(new IconImage(entry, imageBytes));
            }

            return TryCreateIcon(images, out iconBytes);
        }

        private static bool TryReadGroupEntries(byte[] groupData, out GroupIconEntry[] entries)
        {
            entries = null;
            if (!HasRange(groupData, 0, GroupIconDirectoryHeaderSize) ||
                ReadUInt16(groupData, 0) != 0 ||
                ReadUInt16(groupData, 2) != 1)
            {
                return false;
            }

            int count = ReadUInt16(groupData, 4);
            if (count <= 0 || count > MaximumIconCount ||
                !HasRange(groupData, GroupIconDirectoryHeaderSize, checked(count * GroupIconDirectoryEntrySize)))
            {
                return false;
            }

            entries = new GroupIconEntry[count];
            for (int index = 0; index < count; index++)
            {
                int offset = GroupIconDirectoryHeaderSize + index * GroupIconDirectoryEntrySize;
                entries[index] = new GroupIconEntry(
                    groupData[offset],
                    groupData[offset + 1],
                    groupData[offset + 2],
                    groupData[offset + 3],
                    ReadUInt16(groupData, offset + 4),
                    ReadUInt16(groupData, offset + 6),
                    ReadUInt16(groupData, offset + 12));
            }

            return true;
        }

        private static bool TryCreateIcon(List<IconImage> images, out byte[] iconBytes)
        {
            iconBytes = null;
            if (images.Count == 0 || images.Count > MaximumIconCount)
            {
                return false;
            }

            int directorySize = checked(GroupIconDirectoryHeaderSize + images.Count * IconDirectoryEntrySize);
            int totalSize = directorySize;
            foreach (IconImage image in images)
            {
                totalSize = checked(totalSize + image.Bytes.Length);
                if (totalSize > MaximumIconBytes)
                {
                    return false;
                }
            }

            iconBytes = new byte[totalSize];
            WriteUInt16(iconBytes, 0, 0);
            WriteUInt16(iconBytes, 2, 1);
            WriteUInt16(iconBytes, 4, checked((ushort)images.Count));

            int imageOffset = directorySize;
            for (int index = 0; index < images.Count; index++)
            {
                IconImage image = images[index];
                int entryOffset = GroupIconDirectoryHeaderSize + index * IconDirectoryEntrySize;
                iconBytes[entryOffset] = image.Entry.Width;
                iconBytes[entryOffset + 1] = image.Entry.Height;
                iconBytes[entryOffset + 2] = image.Entry.ColorCount;
                iconBytes[entryOffset + 3] = image.Entry.Reserved;
                WriteUInt16(iconBytes, entryOffset + 4, image.Entry.Planes);
                WriteUInt16(iconBytes, entryOffset + 6, image.Entry.BitCount);
                WriteUInt32(iconBytes, entryOffset + 8, checked((uint)image.Bytes.Length));
                WriteUInt32(iconBytes, entryOffset + 12, checked((uint)imageOffset));
                image.Bytes.CopyTo(iconBytes, imageOffset);
                imageOffset += image.Bytes.Length;
            }

            return true;
        }

        private static bool TryReadResourceData(
            PEReader peReader,
            byte[] resources,
            int dataEntryOffset,
            out byte[] data)
        {
            data = null;
            if (!HasRange(resources, dataEntryOffset, ResourceDataEntrySize))
            {
                return false;
            }

            uint dataRva = ReadUInt32(resources, dataEntryOffset);
            uint dataSize = ReadUInt32(resources, dataEntryOffset + 4);
            if (dataRva == 0 || dataSize == 0 || dataRva > int.MaxValue ||
                dataSize > MaximumIconBytes || dataSize > int.MaxValue)
            {
                return false;
            }

            data = peReader
                .GetSectionData(checked((int)dataRva))
                .GetContent(0, checked((int)dataSize))
                .ToArray();
            return data.Length == dataSize;
        }

        private static bool TryFindIdDirectory(
            byte[] resources,
            int directoryOffset,
            int resourceId,
            out int childDirectoryOffset)
        {
            childDirectoryOffset = 0;
            if (!TryGetDirectoryEntries(resources, directoryOffset, out int entriesOffset, out int entryCount))
            {
                return false;
            }

            for (int index = 0; index < entryCount; index++)
            {
                int entryOffset = entriesOffset + index * ResourceDirectoryEntrySize;
                uint name = ReadUInt32(resources, entryOffset);
                uint target = ReadUInt32(resources, entryOffset + 4);
                if ((name & DirectoryFlag) == 0 && name == resourceId &&
                    (target & DirectoryFlag) != 0 &&
                    TryGetRelativeOffset(target, out childDirectoryOffset))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindFirstDirectory(
            byte[] resources,
            int directoryOffset,
            out int childDirectoryOffset,
            out int resourceId)
        {
            childDirectoryOffset = 0;
            resourceId = -1;
            if (!TryGetDirectoryEntries(resources, directoryOffset, out int entriesOffset, out int entryCount))
            {
                return false;
            }

            for (int index = 0; index < entryCount; index++)
            {
                int entryOffset = entriesOffset + index * ResourceDirectoryEntrySize;
                uint name = ReadUInt32(resources, entryOffset);
                uint target = ReadUInt32(resources, entryOffset + 4);
                if ((target & DirectoryFlag) != 0 && TryGetRelativeOffset(target, out childDirectoryOffset))
                {
                    resourceId = (name & DirectoryFlag) == 0 && name <= int.MaxValue ? (int)name : -1;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindFirstDataEntry(
            byte[] resources,
            int directoryOffset,
            out int dataEntryOffset,
            out int resourceId)
        {
            dataEntryOffset = 0;
            resourceId = -1;
            if (!TryGetDirectoryEntries(resources, directoryOffset, out int entriesOffset, out int entryCount))
            {
                return false;
            }

            for (int index = 0; index < entryCount; index++)
            {
                int entryOffset = entriesOffset + index * ResourceDirectoryEntrySize;
                uint name = ReadUInt32(resources, entryOffset);
                uint target = ReadUInt32(resources, entryOffset + 4);
                if ((target & DirectoryFlag) == 0 && TryGetRelativeOffset(target, out dataEntryOffset))
                {
                    resourceId = (name & DirectoryFlag) == 0 && name <= int.MaxValue ? (int)name : -1;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindDataEntry(
            byte[] resources,
            int directoryOffset,
            int preferredResourceId,
            out int dataEntryOffset)
        {
            dataEntryOffset = 0;
            if (!TryGetDirectoryEntries(resources, directoryOffset, out int entriesOffset, out int entryCount))
            {
                return false;
            }

            int fallbackOffset = 0;
            for (int index = 0; index < entryCount; index++)
            {
                int entryOffset = entriesOffset + index * ResourceDirectoryEntrySize;
                uint name = ReadUInt32(resources, entryOffset);
                uint target = ReadUInt32(resources, entryOffset + 4);
                if ((target & DirectoryFlag) != 0 || !TryGetRelativeOffset(target, out int candidateOffset))
                {
                    continue;
                }

                fallbackOffset = fallbackOffset == 0 ? candidateOffset : fallbackOffset;
                if ((name & DirectoryFlag) == 0 && name == preferredResourceId)
                {
                    dataEntryOffset = candidateOffset;
                    return true;
                }
            }

            dataEntryOffset = fallbackOffset;
            return dataEntryOffset != 0;
        }

        private static bool TryGetDirectoryEntries(
            byte[] resources,
            int directoryOffset,
            out int entriesOffset,
            out int entryCount)
        {
            entriesOffset = 0;
            entryCount = 0;
            if (!HasRange(resources, directoryOffset, ResourceDirectoryHeaderSize))
            {
                return false;
            }

            int namedCount = ReadUInt16(resources, directoryOffset + 12);
            int idCount = ReadUInt16(resources, directoryOffset + 14);
            entryCount = checked(namedCount + idCount);
            entriesOffset = checked(directoryOffset + ResourceDirectoryHeaderSize);
            return entryCount <= ushort.MaxValue &&
                HasRange(resources, entriesOffset, checked(entryCount * ResourceDirectoryEntrySize));
        }

        private static bool TryGetRelativeOffset(uint value, out int offset)
        {
            uint relativeOffset = value & OffsetMask;
            offset = relativeOffset <= int.MaxValue ? (int)relativeOffset : 0;
            return relativeOffset <= int.MaxValue;
        }

        private static bool HasRange(byte[] data, int offset, int length)
        {
            return offset >= 0 && length >= 0 && offset <= data.Length - length;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
        }

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), value);
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
        }

        private readonly record struct GroupIconEntry(
            byte Width,
            byte Height,
            byte ColorCount,
            byte Reserved,
            ushort Planes,
            ushort BitCount,
            ushort ResourceId);

        private readonly record struct IconImage(GroupIconEntry Entry, byte[] Bytes);
    }
}
