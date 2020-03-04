﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service.Grpc;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <nodoc />
    public class ProactiveCopyResult : ResultBase
    {
        /// <nodoc />
        public bool WasProactiveCopyNeeded { get; }

        /// <nodoc />
        public PushFileResult RingCopyResult { get; }

        /// <nodoc />
        public PushFileResult OutsideRingCopyResult { get; }

        /// <nodoc />
        public static ProactiveCopyResult CopyNotRequiredResult { get; } = new ProactiveCopyResult();

        private ProactiveCopyResult()
        {
            WasProactiveCopyNeeded = false;
        }

        /// <nodoc />
        public ProactiveCopyResult(PushFileResult ringCopyResult, PushFileResult outsideRingCopyResult)
            : base(GetErrorMessage(ringCopyResult, outsideRingCopyResult), GetDiagnostics(ringCopyResult, outsideRingCopyResult))
        {
            WasProactiveCopyNeeded = true;
            RingCopyResult = ringCopyResult;
            OutsideRingCopyResult = outsideRingCopyResult;
        }

        /// <nodoc />
        public ProactiveCopyResult(ResultBase other, string message = null)
            : base(other, message)
        {
        }

        private static string GetErrorMessage(PushFileResult ringCopyResult, PushFileResult outsideRingCopyResult)
        {
            if (!ringCopyResult.Succeeded || !outsideRingCopyResult.Succeeded)
            {
                return
                    $"Success count: {(ringCopyResult.Succeeded ^ outsideRingCopyResult.Succeeded ? 1 : 0)} " +
                    $"RingMachineResult=[{ringCopyResult.GetSuccessOrErrorMessage()}] " +
                    $"OutsideRingMachineResult=[{outsideRingCopyResult.GetSuccessOrErrorMessage()}] ";
            }

            return null;
        }

        private static string GetDiagnostics(PushFileResult ringCopyResult, PushFileResult outsideRingCopyResult)
        {
            if (!ringCopyResult.Succeeded || !outsideRingCopyResult.Succeeded)
            {
                return
                    $"RingMachineResult=[{ringCopyResult.GetSuccessOrDiagnostics()}] " +
                    $"OutsideRingMachineResult=[{ringCopyResult.GetSuccessOrDiagnostics()}] ";
            }

            return null;
        }

        /// <inheritdoc />
        protected override string GetSuccessString()
        {
            return WasProactiveCopyNeeded ? $"Success" : "Success: No copy needed";
        }
    }
}
