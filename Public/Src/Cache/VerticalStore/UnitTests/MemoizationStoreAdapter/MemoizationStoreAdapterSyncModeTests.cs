// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Tests
{
    // TODO: Merge with MemoizationStoreAdapterLocalCacheTests after verifying that SynchronizationMode of Normal is safe/fast enough
    public class MemoizationStoreAdapterSyncModeTests : TestMemoizationStoreAdapterLocalCacheBase
    {
        protected override string DefaultMemoizationStoreJsonConfigString => @"{{
            ""Assembly"":""BuildXL.Cache.MemoizationStoreAdapter"",
            ""Type"":""BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory"",
            ""CacheId"":""{0}"",
            ""MaxCacheSizeInMB"":{1},
            ""MaxStrongFingerprints"":{2},
            ""CacheRootPath"":""{3}"",
            ""CacheLogPath"":""{4}"",
            ""SingleInstanceTimeoutInSeconds"":10,
            ""ApplyDenyWriteAttributesOnContent"":""true"",
            ""SynchronizationMode"":""Normal""
        }}";
    }
}
