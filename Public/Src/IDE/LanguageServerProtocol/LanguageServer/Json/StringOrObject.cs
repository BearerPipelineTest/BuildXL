// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace LanguageServer.Json
{
    /// <nodoc />
    public sealed class StringOrObject<TObject> : Either<string, TObject>
    {
        /// <nodoc />
        public static implicit operator StringOrObject<TObject>(string left)
            => new StringOrObject<TObject>(left);

        /// <nodoc />
        public static implicit operator StringOrObject<TObject>(TObject right)
            => new StringOrObject<TObject>(right);

        /// <nodoc />
        public StringOrObject()
        {
        }

        /// <nodoc />
        public StringOrObject(string left)
            : base(left)
        {
        }

        /// <nodoc />
        public StringOrObject(TObject right)
            : base(right)
        {
        }

        /// <nodoc />
        protected override EitherTag OnDeserializing(JsonDataType jsonType)
        {
            return
                (jsonType == JsonDataType.String) ? EitherTag.Left :
                (jsonType == JsonDataType.Object) ? EitherTag.Right :
                EitherTag.None;
        }
    }
}
