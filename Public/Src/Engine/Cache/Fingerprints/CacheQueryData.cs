// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Data used as keys for querying the cache.
    /// </summary>
    /// <remarks>This data includes weak/strong fingerprints and metadata hash.</remarks>
    public sealed class CacheQueryData
    {
        /// <summary>
        /// Weak fingerprint.
        /// </summary>
        public WeakContentFingerprint WeakContentFingerprint;

        /// <summary>
        /// Path set hash.
        /// </summary>
        public ContentHash PathSetHash;

        /// <summary>
        /// Strong fingerprint.
        /// </summary>
        public StrongContentFingerprint StrongContentFingerprint;
        
        /// <summary>
        /// Metadata hash.
        /// </summary>
        public ContentHash MetadataHash;

        /// <summary>
        /// Content cache.
        /// </summary>
        public IArtifactContentCache ContentCache;
    }
}
