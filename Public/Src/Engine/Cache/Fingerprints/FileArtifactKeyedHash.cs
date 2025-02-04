// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Engine.Distribution.OpenBond
{
    /// <nodoc/>
    public partial class FileArtifactKeyedHash
    {
        /// <summary>
        /// Gets the file artifact
        /// </summary>
        public FileArtifact File
        {
            get
            {
                return new FileArtifact(new AbsolutePath(PathValue), RewriteCount);
            }

            set
            {
                PathValue = value.Path.Value.Value;
                RewriteCount = value.RewriteCount;
            }
        }

        /// <nodoc/>
        public FileArtifactKeyedHash SetFileMaterializationInfo(PathTable pathTable, FileMaterializationInfo info)
        {
            Length = info.FileContentInfo.SerializedLengthAndExistence;
            ContentHash = info.Hash.ToBondContentHash();
            FileName = info.FileName.IsValid ? info.FileName.ToString(pathTable.StringTable) : null;
            ReparsePointType = info.ReparsePointInfo.ReparsePointType.ToBondReparsePointType();
            ReparsePointTarget = info.ReparsePointInfo.GetReparsePointTarget();
            IsAllowedFileRewrite = info.IsUndeclaredFileRewrite;
            IsExecutable = info.IsExecutable;

            return this;
        }

        /// <nodoc/>
        public FileMaterializationInfo GetFileMaterializationInfo(PathTable pathTable)
        {
            return new FileMaterializationInfo(
                new FileContentInfo(ContentHash.ToContentHash(), FileContentInfo.LengthAndExistence.Deserialize(Length)),
                !string.IsNullOrEmpty(FileName) ? PathAtom.Create(pathTable.StringTable, FileName) : PathAtom.Invalid,
                ReparsePointInfo.Create(ReparsePointType.ToReparsePointType(), ReparsePointTarget),
                IsAllowedFileRewrite,
                IsExecutable);
        }
    }
}
