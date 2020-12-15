﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Reloaded.Memory.Streams;
using RidersArchiveTool.Structs.Parser;

namespace RidersArchiveTool
{
    /// <summary>
    /// Reads an individual Riders Archive.
    /// </summary>
    public class ArchiveReader : IDisposable
    {
        /// <summary>
        /// All of the groups which belong to this archive.
        /// </summary>
        public Group[] Groups;

        private long _startPos;
        private Stream _stream;

        /// <summary>
        /// Reads an archive from a stream.
        /// </summary>
        /// <param name="stream">Stream pointing to the start of the archive.</param>
        /// <param name="archiveSize">Size of the archive file.</param>
        public ArchiveReader(Stream stream, int archiveSize)
        {
            _stream = stream;
            _startPos = stream.Position;

            // Extract Data.
            using var streamReader = new BufferedStreamReader(stream, 2048);
            streamReader.Read(out int binCount);
            Groups = new Group[binCount];

            // Get group item counts.
            for (int x = 0; x < Groups.Length; x++)
                Groups[x].Files = new Structs.Parser.File[streamReader.Read<byte>()];

            // Alignment
            streamReader.Seek(Utilities.Utilities.RoundUp((int) streamReader.Position(), 4) - streamReader.Position(), SeekOrigin.Current);

            // Skip section containing first item for each group.
            streamReader.Seek(sizeof(short) * Groups.Length, SeekOrigin.Current);

            // Populate IDs
            for (int x = 0; x < Groups.Length; x++)
                Groups[x].Id = streamReader.Read<ushort>();

            // Populate offsets.
            int[] offsets = new int[Groups.Select(x => x.Files.Length).Sum()];
            for (int x = 0; x < offsets.Length; x++)
                offsets[x] = streamReader.Read<int>();

            int offsetIndex = 0;
            for (int x = 0; x < Groups.Length; x++)
            {
                var fileCount = Groups[x].Files.Length;
                for (int y = 0; y < fileCount; y++)
                {
                    // Do not fill if no more elements left.
                    if (offsetIndex >= offsets.Length)
                        break;
                    
                    var offset = (int) offsets[offsetIndex];
                    int nextOffsetIndex = offsetIndex;
                    offsetIndex += 1;

                    // Find next non-zero value within array; if not found, use archive size..
                    do { nextOffsetIndex += 1; } 
                    while (nextOffsetIndex < offsets.Length && offsets[nextOffsetIndex] == 0);

                    var nextOffset = nextOffsetIndex < offsets.Length ? offsets[nextOffsetIndex] : archiveSize;

                    // Set offsets
                    Groups[x].Files[y].Offset = offset;
                    Groups[x].Files[y].Size   = nextOffset - offset;
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _stream?.Dispose();
        }

        /// <summary>
        /// Gets all files in all groups.
        /// </summary>
        /// <returns>A mapping between group id to all of the group's files.</returns>
        public Dictionary<ushort, byte[][]> GetAllFiles()
        {
            var dict = new Dictionary<ushort, byte[][]>();
            foreach (var group in Groups)
                dict[group.Id] = GetFiles(group);

            return dict;
        }

        /// <summary>
        /// Gets the data belonging to all of the files of the group.
        /// </summary>
        /// <param name="group">The group to which the file belongs to.</param>
        /// <returns>Data for each file. Or an empty array if the file was listed in the archive header but not present in archive (offset/size = 0)</returns>
        public byte[][] GetFiles(Group group)
        {
            byte[][] files = new byte[group.Files.Length][];
            for (int x = 0; x < group.Files.Length; x++)
                files[x] = GetFile(group, x);

            return files;
        }

        /// <summary>
        /// Gets the data belonging to an individual file in a group.
        /// </summary>
        /// <param name="group">The group to which the file belongs to.</param>
        /// <param name="fileIndex">Index of the individual file.</param>
        /// <returns>Data for the file. Or an empty array if the file was listed in the archive header but not present in archive (offset/size = 0)</returns>
        public byte[] GetFile(Group group, int fileIndex)
        {
            var file   = group.Files[fileIndex];
            var offset = file.Offset;
            if (offset <= 0)
                return new byte[0];

            var buffer = new byte[file.Size];
            _stream.Position = _startPos + file.Offset;
            _stream.Read(buffer.AsSpan());
            return buffer;
        }
    }
}