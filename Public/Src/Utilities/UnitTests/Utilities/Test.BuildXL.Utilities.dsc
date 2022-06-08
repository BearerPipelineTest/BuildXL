// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Core {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            // These tests require Detours to run itself, so we won't detour the test runner process itself
            unsafeTestRunArguments: {
                runWithUntrackedDependencies: true
            },
        },
        references: [
            // This is needed for 'FluentAssersions' running on a full framework.
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
                NetFx.System.Runtime.Serialization.dll,
                NetFx.Netstandard.dll
            ),
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("Newtonsoft.Json").pkg,
            AsyncMutexClient.exe,
            ...BuildXLSdk.systemMemoryDeployment,
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, importFrom("BuildXL.Utilities").SBOMUtilities.dll),
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, importFrom("Microsoft.Sbom.Contracts").pkg),
            ...addIf(
                BuildXLSdk.isFullFramework,
                BuildXLSdk.withQualifier({targetFramework: qualifier.targetFramework}).NetFx.Netstandard.dll
            ),
            ...BuildXLSdk.fluentAssertionsWorkaround
        ],
        runtimeContent: [
            AsyncMutexClient.exe,
        ],
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects()
    });
}
