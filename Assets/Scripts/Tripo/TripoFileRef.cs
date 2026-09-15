namespace MusePico.Tripo
{
    /// <summary>
    /// The shape every Tripo endpoint that takes an image or a model expects. Exactly one of
    /// <see cref="Url"/>, <see cref="FileToken"/> or the bucket/key pair is set.
    ///
    /// <see cref="FileToken"/> is the one that matters for a Unity tool: it comes from
    /// <c>POST /v3/files</c>, which means a local PNG can be sent without first being published
    /// somewhere public. muse-infinity could not do this — its multiview route needs
    /// <c>PUBLIC_APP_URL</c> to be a deployed HTTPS root, which is why no character was ever
    /// submitted from a laptop.
    /// </summary>
    public readonly struct TripoFileRef
    {
        public readonly string Url;
        public readonly string FileToken;
        public readonly string Bucket;
        public readonly string ObjectKey;
        /// <summary>Optional format hint ("png", "jpg"). Tripo does not currently validate it.</summary>
        public readonly string Type;

        TripoFileRef(string url, string fileToken, string bucket, string objectKey, string type)
        {
            Url = url;
            FileToken = fileToken;
            Bucket = bucket;
            ObjectKey = objectKey;
            Type = type;
        }

        public static TripoFileRef FromUrl(string url, string type = null) =>
            new TripoFileRef(url, null, null, null, type);

        public static TripoFileRef FromToken(string fileToken, string type = null) =>
            new TripoFileRef(null, fileToken, null, null, type);

        public static TripoFileRef FromObject(string bucket, string key, string type = null) =>
            new TripoFileRef(null, null, bucket, key, type);

        /// <summary>An absolute http(s) URL is a URL; anything else is treated as a file token.</summary>
        public static TripoFileRef Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return default;
            return value.StartsWith("http://") || value.StartsWith("https://")
                ? FromUrl(value)
                : FromToken(value);
        }

        public bool IsEmpty =>
            string.IsNullOrEmpty(Url) && string.IsNullOrEmpty(FileToken) && string.IsNullOrEmpty(Bucket);

        public JsonBuilder ToJson()
        {
            var json = new JsonBuilder()
                .Add("type", string.IsNullOrEmpty(Type) ? null : Type)
                .Add("file_token", string.IsNullOrEmpty(FileToken) ? null : FileToken)
                .Add("url", string.IsNullOrEmpty(Url) ? null : Url);
            if (!string.IsNullOrEmpty(Bucket))
            {
                json.Add("object", new JsonBuilder().Add("bucket", Bucket).Add("key", ObjectKey));
            }
            return json;
        }
    }
}
