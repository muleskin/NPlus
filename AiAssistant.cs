using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace nplus
{
    // ============================================================================
    //  Optional AI assistant.
    //
    //  Entirely opt-in: nothing here runs unless the user enables it and configures
    //  a provider in AI -> Settings. Six backends are supported behind one uniform
    //  HTTP interface (no provider SDKs, so the single-file build is untouched):
    //
    //    OpenAI (ChatGPT) / Azure OpenAI — OpenAI chat-completions wire format
    //    Perplexity / Ollama            — OpenAI-compatible wire format
    //    Claude (Anthropic Messages API) — x-api-key + anthropic-version
    //    Gemini (Google Generative AI)   — contents/parts wire format
    //
    //  The user supplies their own key/endpoint per provider; keys live only in
    //  %AppData%\nplus\ai_settings.json on this machine.
    // ============================================================================

    internal enum AiProvider { OpenAI, AzureOpenAI, Claude, Gemini, Ollama, Perplexity }

    internal enum AiResultAction { None, Replace, NewTab, Copy }

    /// <summary>One chat turn. Role is "user" or "assistant".</summary>
    internal sealed class AiMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public AiMessage() { }
        public AiMessage(string role, string content) { Role = role; Content = content; }
    }

    /// <summary>Per-provider connection settings.</summary>
    internal sealed class AiProviderConfig
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "";   // model id, or Azure deployment name
        public string Endpoint { get; set; } = ""; // Azure resource URL / Ollama base URL
    }

    /// <summary>Top-level AI configuration, persisted as JSON.</summary>
    internal sealed class AiSettings
    {
        public bool Enabled { get; set; } = false;
        public AiProvider Provider { get; set; } = AiProvider.OpenAI;
        public Dictionary<AiProvider, AiProviderConfig> Providers { get; set; }
            = new Dictionary<AiProvider, AiProviderConfig>();

        /// <summary>Returns the config for a provider, creating sensible defaults on first use.</summary>
        public AiProviderConfig For(AiProvider p)
        {
            if (!Providers.TryGetValue(p, out AiProviderConfig cfg) || cfg == null)
            {
                cfg = new AiProviderConfig
                {
                    Model = AiDefaults.Model(p),
                    Endpoint = AiDefaults.Endpoint(p),
                };
                Providers[p] = cfg;
            }
            // Backfill blanks for configs saved before a default existed.
            if (string.IsNullOrWhiteSpace(cfg.Model)) cfg.Model = AiDefaults.Model(p);
            if (string.IsNullOrWhiteSpace(cfg.Endpoint)) cfg.Endpoint = AiDefaults.Endpoint(p);
            return cfg;
        }

        public static AiSettings Load(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    var s = JsonSerializer.Deserialize<AiSettings>(System.IO.File.ReadAllText(path));
                    if (s != null) { s.DecryptKeys(); return s; }
                }
            }
            catch { /* corrupt/old file — fall through to defaults */ }
            return new AiSettings();
        }

        public void Save(string path)
        {
            try
            {
                // Persist a copy with the API keys DPAPI-encrypted, so they never hit
                // disk in cleartext. The live object keeps its plaintext keys for use
                // at request time.
                AiSettings toSave = Clone();
                toSave.EncryptKeys();
                System.IO.File.WriteAllText(path,
                    JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best-effort; never block the UI on a settings write */ }
        }

        /// <summary>Encrypts every provider's key in place (called on a save-only copy).</summary>
        private void EncryptKeys()
        {
            if (Providers == null) return;
            foreach (AiProviderConfig cfg in Providers.Values)
                if (cfg != null) cfg.ApiKey = AiSecret.Protect(cfg.ApiKey);
        }

        /// <summary>
        /// Decrypts every provider's key in place (called right after load). Keys saved
        /// in cleartext by an older build carry no marker and are passed through as-is,
        /// then re-saved encrypted on the next write.
        /// </summary>
        private void DecryptKeys()
        {
            if (Providers == null) return;
            foreach (AiProviderConfig cfg in Providers.Values)
                if (cfg != null) cfg.ApiKey = AiSecret.Unprotect(cfg.ApiKey);
        }

        /// <summary>Deep copy via JSON round-trip — used so the settings dialog edits a working copy.</summary>
        public AiSettings Clone()
        {
            return JsonSerializer.Deserialize<AiSettings>(JsonSerializer.Serialize(this)) ?? new AiSettings();
        }
    }

    /// <summary>
    /// Encrypts/decrypts AI API keys for at-rest storage using Windows DPAPI
    /// (CurrentUser scope) — the ciphertext can only be read back by the same Windows
    /// user on the same machine. Encrypted values are tagged with a marker prefix so a
    /// legacy cleartext key (no marker) is recognised and transparently migrated.
    /// </summary>
    internal static class AiSecret
    {
        private const string Marker = "dpapi:";

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return plain ?? "";
            try
            {
                byte[] enc = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return Marker + Convert.ToBase64String(enc);
            }
            catch
            {
                // DPAPI unavailable for some reason — persisting the key (as before)
                // beats silently dropping it and breaking the user's configured provider.
                return plain;
            }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored ?? "";
            if (!stored.StartsWith(Marker, StringComparison.Ordinal))
                return stored;   // legacy cleartext; re-saved encrypted on next write
            try
            {
                byte[] enc = Convert.FromBase64String(stored.Substring(Marker.Length));
                byte[] plain = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                // Corrupt, tampered, or encrypted by a different user/machine.
                return "";
            }
        }
    }

    internal static class AiDefaults
    {
        public static string Model(AiProvider p)
        {
            switch (p)
            {
                case AiProvider.OpenAI: return "gpt-4o";
                case AiProvider.AzureOpenAI: return "";              // user's deployment name
                case AiProvider.Claude: return "claude-opus-4-8";
                case AiProvider.Gemini: return "gemini-2.0-flash";
                case AiProvider.Ollama: return "llama3.1";
                case AiProvider.Perplexity: return "sonar";
                default: return "";
            }
        }

        public static string Endpoint(AiProvider p)
        {
            switch (p)
            {
                case AiProvider.AzureOpenAI: return "https://YOUR-RESOURCE.openai.azure.com";
                case AiProvider.Ollama: return "http://localhost:11434";
                default: return "";
            }
        }

        public static string Display(AiProvider p)
        {
            switch (p)
            {
                case AiProvider.OpenAI: return "OpenAI (ChatGPT)";
                case AiProvider.AzureOpenAI: return "Azure OpenAI";
                case AiProvider.Claude: return "Claude (Anthropic)";
                case AiProvider.Gemini: return "Gemini (Google)";
                case AiProvider.Ollama: return "Ollama (local)";
                case AiProvider.Perplexity: return "Perplexity";
                default: return p.ToString();
            }
        }

        /// <summary>Ollama runs locally with no key; everything else needs one.</summary>
        public static bool NeedsApiKey(AiProvider p) => p != AiProvider.Ollama;
    }

    /// <summary>Sends chat requests to the configured provider over raw HTTP.</summary>
    internal sealed class AiClient
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        private const string AzureApiVersion = "2024-10-21";

        public async Task<string> CompleteAsync(
            AiSettings settings, IList<AiMessage> messages, string system, CancellationToken ct)
        {
            AiProviderConfig cfg = settings.For(settings.Provider);
            switch (settings.Provider)
            {
                case AiProvider.OpenAI:
                    return await OpenAiStyleAsync("https://api.openai.com/v1/chat/completions",
                        cfg, messages, system, useBearer: true, modelInBody: true, ct);
                case AiProvider.Perplexity:
                    return await OpenAiStyleAsync("https://api.perplexity.ai/chat/completions",
                        cfg, messages, system, useBearer: true, modelInBody: true, ct);
                case AiProvider.Ollama:
                    return await OllamaAsync(cfg, messages, system, ct);
                case AiProvider.AzureOpenAI:
                    return await AzureAsync(cfg, messages, system, ct);
                case AiProvider.Claude:
                    return await ClaudeAsync(cfg, messages, system, ct);
                case AiProvider.Gemini:
                    return await GeminiAsync(cfg, messages, system, ct);
                default:
                    throw new InvalidOperationException("Unknown provider.");
            }
        }

        // --- OpenAI / Perplexity (and the body shape Azure reuses) ---------------

        private static List<object> OpenAiMessages(IList<AiMessage> messages, string system)
        {
            var list = new List<object>();
            if (!string.IsNullOrWhiteSpace(system))
                list.Add(new { role = "system", content = system });
            foreach (var m in messages)
                list.Add(new { role = m.Role, content = m.Content });
            return list;
        }

        private async Task<string> OpenAiStyleAsync(string url, AiProviderConfig cfg,
            IList<AiMessage> messages, string system, bool useBearer, bool modelInBody, CancellationToken ct)
        {
            var body = new Dictionary<string, object> { ["messages"] = OpenAiMessages(messages, system) };
            if (modelInBody) body["model"] = cfg.Model;

            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };
            if (useBearer)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);

            return ExtractOpenAiText(await SendAsync(req, ct));
        }

        private async Task<string> AzureAsync(AiProviderConfig cfg,
            IList<AiMessage> messages, string system, CancellationToken ct)
        {
            string baseUrl = (cfg.Endpoint ?? "").TrimEnd('/');
            string url = $"{baseUrl}/openai/deployments/{cfg.Model}/chat/completions?api-version={AzureApiVersion}";
            var body = new Dictionary<string, object> { ["messages"] = OpenAiMessages(messages, system) };

            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };
            req.Headers.TryAddWithoutValidation("api-key", cfg.ApiKey);

            return ExtractOpenAiText(await SendAsync(req, ct));
        }

        private async Task<string> OllamaAsync(AiProviderConfig cfg,
            IList<AiMessage> messages, string system, CancellationToken ct)
        {
            string baseUrl = string.IsNullOrWhiteSpace(cfg.Endpoint) ? "http://localhost:11434" : cfg.Endpoint.TrimEnd('/');
            var body = new Dictionary<string, object>
            {
                ["model"] = cfg.Model,
                ["messages"] = OpenAiMessages(messages, system),
                ["stream"] = false,
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/chat") { Content = JsonContent(body) };
            using JsonDocument doc = JsonDocument.Parse(await SendAsync(req, ct));
            return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        // --- Claude (Anthropic Messages API) -------------------------------------

        private async Task<string> ClaudeAsync(AiProviderConfig cfg,
            IList<AiMessage> messages, string system, CancellationToken ct)
        {
            var msgs = new List<object>();
            foreach (var m in messages) msgs.Add(new { role = m.Role, content = m.Content });

            var body = new Dictionary<string, object>
            {
                ["model"] = cfg.Model,
                ["max_tokens"] = 4096,
                ["messages"] = msgs,
            };
            if (!string.IsNullOrWhiteSpace(system)) body["system"] = system;

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            { Content = JsonContent(body) };
            req.Headers.TryAddWithoutValidation("x-api-key", cfg.ApiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            using JsonDocument doc = JsonDocument.Parse(await SendAsync(req, ct));
            var sb = new StringBuilder();
            foreach (JsonElement block in doc.RootElement.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text")
                    sb.Append(block.GetProperty("text").GetString());
            }
            return sb.ToString();
        }

        // --- Gemini (Google Generative Language API) -----------------------------

        private async Task<string> GeminiAsync(AiProviderConfig cfg,
            IList<AiMessage> messages, string system, CancellationToken ct)
        {
            var contents = new List<object>();
            foreach (var m in messages)
            {
                contents.Add(new
                {
                    role = m.Role == "assistant" ? "model" : "user",
                    parts = new[] { new { text = m.Content } },
                });
            }

            var body = new Dictionary<string, object> { ["contents"] = contents };
            if (!string.IsNullOrWhiteSpace(system))
                body["systemInstruction"] = new { parts = new[] { new { text = system } } };

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{cfg.Model}:generateContent?key={Uri.EscapeDataString(cfg.ApiKey)}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };

            using JsonDocument doc = JsonDocument.Parse(await SendAsync(req, ct));
            var sb = new StringBuilder();
            JsonElement candidates = doc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() > 0)
            {
                foreach (JsonElement part in candidates[0].GetProperty("content").GetProperty("parts").EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var txt)) sb.Append(txt.GetString());
                }
            }
            return sb.ToString();
        }

        // --- Streaming (token-by-token) ------------------------------------------
        //
        // onToken is invoked for each text chunk as it arrives. Because callers await
        // this without ConfigureAwait(false), continuations resume on the WinForms UI
        // thread, so onToken can touch controls directly. Returns the full text.

        public async Task<string> CompleteStreamAsync(
            AiSettings settings, IList<AiMessage> messages, string system, Action<string> onToken, CancellationToken ct)
        {
            AiProviderConfig cfg = settings.For(settings.Provider);
            switch (settings.Provider)
            {
                case AiProvider.OpenAI:
                    return await OpenAiStreamAsync("https://api.openai.com/v1/chat/completions", cfg, messages, system, useBearer: true, ct, onToken);
                case AiProvider.Perplexity:
                    return await OpenAiStreamAsync("https://api.perplexity.ai/chat/completions", cfg, messages, system, useBearer: true, ct, onToken);
                case AiProvider.AzureOpenAI:
                    return await AzureStreamAsync(cfg, messages, system, ct, onToken);
                case AiProvider.Ollama:
                    return await OllamaStreamAsync(cfg, messages, system, ct, onToken);
                case AiProvider.Claude:
                    return await ClaudeStreamAsync(cfg, messages, system, ct, onToken);
                case AiProvider.Gemini:
                    return await GeminiStreamAsync(cfg, messages, system, ct, onToken);
                default:
                    throw new InvalidOperationException("Unknown provider.");
            }
        }

        private async Task<string> OpenAiStreamAsync(string url, AiProviderConfig cfg,
            IList<AiMessage> messages, string system, bool useBearer, CancellationToken ct, Action<string> onToken)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = cfg.Model,
                ["messages"] = OpenAiMessages(messages, system),
                ["stream"] = true,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };
            if (useBearer) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
            return await ReadSseAsync(req, ct, onToken, PickOpenAi);
        }

        private async Task<string> AzureStreamAsync(AiProviderConfig cfg,
            IList<AiMessage> messages, string system, CancellationToken ct, Action<string> onToken)
        {
            string baseUrl = (cfg.Endpoint ?? "").TrimEnd('/');
            string url = $"{baseUrl}/openai/deployments/{cfg.Model}/chat/completions?api-version={AzureApiVersion}";
            var body = new Dictionary<string, object>
            {
                ["messages"] = OpenAiMessages(messages, system),
                ["stream"] = true,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };
            req.Headers.TryAddWithoutValidation("api-key", cfg.ApiKey);
            return await ReadSseAsync(req, ct, onToken, PickOpenAi);
        }

        private async Task<string> ClaudeStreamAsync(AiProviderConfig cfg,
            IList<AiMessage> messages, string system, CancellationToken ct, Action<string> onToken)
        {
            var msgs = new List<object>();
            foreach (var m in messages) msgs.Add(new { role = m.Role, content = m.Content });
            var body = new Dictionary<string, object>
            {
                ["model"] = cfg.Model,
                ["max_tokens"] = 4096,
                ["messages"] = msgs,
                ["stream"] = true,
            };
            if (!string.IsNullOrWhiteSpace(system)) body["system"] = system;

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            { Content = JsonContent(body) };
            req.Headers.TryAddWithoutValidation("x-api-key", cfg.ApiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            return await ReadSseAsync(req, ct, onToken, PickClaude);
        }

        private async Task<string> GeminiStreamAsync(AiProviderConfig cfg,
            IList<AiMessage> messages, string system, CancellationToken ct, Action<string> onToken)
        {
            var contents = new List<object>();
            foreach (var m in messages)
                contents.Add(new { role = m.Role == "assistant" ? "model" : "user", parts = new[] { new { text = m.Content } } });

            var body = new Dictionary<string, object> { ["contents"] = contents };
            if (!string.IsNullOrWhiteSpace(system))
                body["systemInstruction"] = new { parts = new[] { new { text = system } } };

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{cfg.Model}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(cfg.ApiKey)}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };
            return await ReadSseAsync(req, ct, onToken, PickGemini);
        }

        private async Task<string> OllamaStreamAsync(AiProviderConfig cfg,
            IList<AiMessage> messages, string system, CancellationToken ct, Action<string> onToken)
        {
            string baseUrl = string.IsNullOrWhiteSpace(cfg.Endpoint) ? "http://localhost:11434" : cfg.Endpoint.TrimEnd('/');
            var body = new Dictionary<string, object>
            {
                ["model"] = cfg.Model,
                ["messages"] = OpenAiMessages(messages, system),
                ["stream"] = true,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/chat") { Content = JsonContent(body) };
            return await ReadNdjsonAsync(req, ct, onToken);
        }

        // Reads a Server-Sent-Events stream ("data: {json}" lines) and accumulates text.
        private async Task<string> ReadSseAsync(HttpRequestMessage req, CancellationToken ct,
            Action<string> onToken, Func<JsonElement, string> pick)
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync();
                throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {TryExtractError(body)}");
            }

            using Stream stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            var sb = new StringBuilder();
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
                string data = line.Substring(5).Trim();
                if (data.Length == 0 || data == "[DONE]") { if (data == "[DONE]") break; continue; }

                string chunk;
                try { using JsonDocument d = JsonDocument.Parse(data); chunk = pick(d.RootElement); }
                catch { continue; }   // ignore keep-alive / non-JSON lines

                if (!string.IsNullOrEmpty(chunk)) { sb.Append(chunk); onToken?.Invoke(chunk); }
            }
            return sb.ToString();
        }

        // Reads an NDJSON stream (one JSON object per line) — Ollama's format.
        private async Task<string> ReadNdjsonAsync(HttpRequestMessage req, CancellationToken ct, Action<string> onToken)
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync();
                throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {TryExtractError(body)}");
            }

            using Stream stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            var sb = new StringBuilder();
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.Length == 0) continue;
                try
                {
                    using JsonDocument d = JsonDocument.Parse(line);
                    if (d.RootElement.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("content", out var content))
                    {
                        string chunk = content.GetString();
                        if (!string.IsNullOrEmpty(chunk)) { sb.Append(chunk); onToken?.Invoke(chunk); }
                    }
                    if (d.RootElement.TryGetProperty("done", out var done) &&
                        done.ValueKind == JsonValueKind.True) break;
                }
                catch { continue; }
            }
            return sb.ToString();
        }

        private static string PickOpenAi(JsonElement root)
        {
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                return c.GetString();
            return null;
        }

        private static string PickClaude(JsonElement root)
        {
            if (root.TryGetProperty("type", out var t) && t.GetString() == "content_block_delta" &&
                root.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("text", out var txt))
                return txt.GetString();
            return null;
        }

        private static string PickGemini(JsonElement root)
        {
            if (!root.TryGetProperty("candidates", out var cand) || cand.GetArrayLength() == 0) return null;
            if (!cand[0].TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts)) return null;
            var sb = new StringBuilder();
            foreach (JsonElement p in parts.EnumerateArray())
                if (p.TryGetProperty("text", out var x)) sb.Append(x.GetString());
            return sb.ToString();
        }

        // --- Shared HTTP plumbing ------------------------------------------------

        private static StringContent JsonContent(object body)
            => new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static string ExtractOpenAiText(string responseBody)
        {
            using JsonDocument doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        private async Task<string> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);
            string text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                string detail = TryExtractError(text);
                throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {detail}");
            }
            return text;
        }

        // Most providers nest a human message under error.message or error[0].message.
        private static string TryExtractError(string body)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    if (err.ValueKind == JsonValueKind.String) return err.GetString();
                    if (err.TryGetProperty("message", out var msg)) return msg.GetString();
                }
            }
            catch { /* not JSON */ }
            return body.Length > 400 ? body.Substring(0, 400) : body;
        }
    }

    /// <summary>Configure provider, key, model, and the master on/off switch.</summary>
    internal sealed class AiSettingsDialog : Form
    {
        private readonly AiSettings _working;
        private readonly CheckBox _enabled;
        private readonly ComboBox _provider;
        private readonly TextBox _apiKey;
        private readonly TextBox _model;
        private readonly TextBox _endpoint;
        private readonly Label _apiKeyLabel, _modelLabel, _endpointLabel;
        private readonly Button _testBtn;
        private readonly AiClient _testClient = new AiClient();

        public AiSettings Result => _working;

        public AiSettingsDialog(AiSettings current)
        {
            _working = current.Clone();

            Text = "AI Assistant Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(460, 300);
            ShowInTaskbar = false;

            _enabled = new CheckBox
            {
                Text = "Enable AI features",
                Left = 16, Top = 14, Width = 420, Checked = _working.Enabled,
            };

            var providerLabel = new Label { Text = "Provider", Left = 16, Top = 50, Width = 110 };
            _provider = new ComboBox
            {
                Left = 130, Top = 47, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList,
            };
            foreach (AiProvider p in Enum.GetValues(typeof(AiProvider)))
                _provider.Items.Add(AiDefaults.Display(p));
            _provider.SelectedIndex = (int)_working.Provider;
            _provider.SelectedIndexChanged += (s, e) => LoadProviderFields();

            _apiKeyLabel = new Label { Text = "API Key", Left = 16, Top = 90, Width = 110 };
            _apiKey = new TextBox { Left = 130, Top = 87, Width = 300, UseSystemPasswordChar = true };

            _modelLabel = new Label { Text = "Model", Left = 16, Top = 126, Width = 110 };
            _model = new TextBox { Left = 130, Top = 123, Width = 300 };

            _endpointLabel = new Label { Text = "Endpoint", Left = 16, Top = 162, Width = 110 };
            _endpoint = new TextBox { Left = 130, Top = 159, Width = 300 };

            var note = new Label
            {
                Left = 16, Top = 196, Width = 420, Height = 50,
                ForeColor = SystemColors.GrayText,
                Text = "Keys are stored locally in %AppData%\\nplus\\ai_settings.json and sent only "
                     + "to the selected provider. Each provider keeps its own key/model.",
            };

            _testBtn = new Button { Text = "Test Connection", Left = 16, Top = 256, Width = 120, Height = 24 };
            _testBtn.Click += (s, e) => TestConnection();

            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 274, Top = 256, Width = 75 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 355, Top = 256, Width = 75 };
            ok.Click += (s, e) => Commit();

            Controls.AddRange(new Control[]
            {
                _enabled, providerLabel, _provider,
                _apiKeyLabel, _apiKey, _modelLabel, _model, _endpointLabel, _endpoint,
                note, _testBtn, ok, cancel,
            });
            AcceptButton = ok;
            CancelButton = cancel;

            LoadProviderFields();
        }

        private AiProvider Selected => (AiProvider)_provider.SelectedIndex;

        private void LoadProviderFields()
        {
            AiProviderConfig cfg = _working.For(Selected);
            _apiKey.Text = cfg.ApiKey;
            _model.Text = cfg.Model;
            _endpoint.Text = cfg.Endpoint;

            bool needsKey = AiDefaults.NeedsApiKey(Selected);
            _apiKey.Enabled = needsKey;
            _apiKeyLabel.Enabled = needsKey;

            bool isAzure = Selected == AiProvider.AzureOpenAI;
            bool usesEndpoint = isAzure || Selected == AiProvider.Ollama;
            _endpoint.Enabled = usesEndpoint;
            _endpointLabel.Enabled = usesEndpoint;

            _modelLabel.Text = isAzure ? "Deployment" : "Model";
            _endpointLabel.Text = isAzure ? "Resource URL" : "Endpoint";
        }

        private void Commit()
        {
            // Persist whatever is on screen back into the selected provider's config.
            AiProviderConfig cfg = _working.For(Selected);
            cfg.ApiKey = _apiKey.Text.Trim();
            cfg.Model = _model.Text.Trim();
            cfg.Endpoint = _endpoint.Text.Trim();
            _working.Provider = Selected;
            _working.Enabled = _enabled.Checked;
        }

        // Sends a tiny probe to the currently-selected provider using the on-screen
        // fields (independent of the Enable checkbox) and reports the outcome.
        private async void TestConnection()
        {
            Commit();   // capture what's typed into the working config first

            AiProviderConfig cfg = _working.For(Selected);
            if (AiDefaults.NeedsApiKey(Selected) && string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                MessageBox.Show(this, "Enter an API key first.", "Connection Test",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var probe = new List<AiMessage> { new AiMessage("user", "Reply with the single word: OK") };
            string original = _testBtn.Text;
            _testBtn.Enabled = false;
            _testBtn.Text = "Testing...";
            Cursor = Cursors.WaitCursor;
            try
            {
                string reply = await _testClient.CompleteAsync(_working, probe, "", CancellationToken.None);
                MessageBox.Show(this,
                    $"Success — {AiDefaults.Display(Selected)} responded:\n\n{(reply ?? "").Trim()}",
                    "Connection Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Connection failed:\n\n" + ex.Message,
                    "Connection Test", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                _testBtn.Enabled = true;
                _testBtn.Text = original;
            }
        }
    }

    /// <summary>
    /// Shows AI output for a selection action. The text is editable, so the user can
    /// tweak before replacing. Buttons set <see cref="ChosenAction"/> and close.
    /// </summary>
    internal sealed class AiResultDialog : Form
    {
        private readonly TextBox _text;
        public AiResultAction ChosenAction { get; private set; } = AiResultAction.None;
        public string ResultText => _text.Text;

        public AiResultDialog(string title, string result, bool canReplace, bool dark)
        {
            Text = "AI — " + title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 460);
            MinimumSize = new Size(380, 280);
            ShowInTaskbar = false;

            _text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = true,
                Font = new Font(FontFamily.GenericMonospace, 10f),
                Text = result ?? "",
            };
            if (dark)
            {
                _text.BackColor = Color.FromArgb(30, 30, 30);
                _text.ForeColor = Color.Gainsboro;
            }

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var replace = new Button { Text = "Replace Selection", Left = 8, Top = 8, Width = 130, Height = 28, Enabled = canReplace };
            var newTab = new Button { Text = "Open in New Tab", Left = 144, Top = 8, Width = 120, Height = 28 };
            var copy = new Button { Text = "Copy", Left = 270, Top = 8, Width = 70, Height = 28 };
            var close = new Button { Text = "Close", Left = 532, Top = 8, Width = 76, Height = 28, DialogResult = DialogResult.Cancel };

            replace.Click += (s, e) => Choose(AiResultAction.Replace);
            newTab.Click += (s, e) => Choose(AiResultAction.NewTab);
            copy.Click += (s, e) => Choose(AiResultAction.Copy);

            bar.Controls.AddRange(new Control[] { replace, newTab, copy, close });
            Controls.Add(_text);
            Controls.Add(bar);
            CancelButton = close;
        }

        private void Choose(AiResultAction action)
        {
            ChosenAction = action;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>
    /// Dockable conversational chat panel. Keeps its own message history and talks to
    /// the active provider. Optionally attaches the current document as context.
    /// </summary>
    internal sealed class AiChatPanel : Panel
    {
        private readonly Func<AiSettings> _getSettings;
        private readonly Func<string> _getDocText;
        private readonly Func<string, Task<string>> _runAgent;
        private readonly AiClient _client = new AiClient();
        private readonly List<AiMessage> _history = new List<AiMessage>();

        private readonly Label _header;
        private readonly Button _closeBtn;
        private readonly TextBox _output;
        private readonly TextBox _input;
        private readonly Button _send;
        private readonly CheckBox _attachDoc;
        private readonly CheckBox _agentMode;

        public event EventHandler CloseRequested;

        public AiChatPanel(Func<AiSettings> getSettings, Func<string> getDocText, Func<string, Task<string>> runAgent)
        {
            _getSettings = getSettings;
            _getDocText = getDocText;
            _runAgent = runAgent;

            Dock = DockStyle.Fill;

            _header = new Label
            {
                Text = "  AI Assistant",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            _closeBtn = new Button { Text = "✕", Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, Width = 28, Cursor = Cursors.Hand };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
            var headerPanel = new Panel { Dock = DockStyle.Top, Height = 28 };
            headerPanel.Controls.Add(_header);
            headerPanel.Controls.Add(_closeBtn);

            _output = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5f),
            };

            _attachDoc = new CheckBox { Text = "Attach current document as context", Dock = DockStyle.Top, Height = 24, Padding = new Padding(6, 0, 0, 0) };
            _agentMode = new CheckBox { Text = "Agent mode (edit this tab, with preview)", Dock = DockStyle.Top, Height = 24, Padding = new Padding(6, 0, 0, 0) };

            _input = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 9.5f),
            };
            _input.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendMessage(); }
            };
            _send = new Button { Text = "Send (Ctrl+Enter)", Dock = DockStyle.Right, Width = 130 };
            _send.Click += (s, e) => SendMessage();
            var inputBar = new Panel { Dock = DockStyle.Bottom, Height = 80 };
            inputBar.Controls.Add(_input);
            inputBar.Controls.Add(_send);

            Controls.Add(_output);
            Controls.Add(_attachDoc);
            Controls.Add(_agentMode);
            Controls.Add(inputBar);
            Controls.Add(headerPanel);
        }

        public void ClearConversation()
        {
            _history.Clear();
            _output.Clear();
        }

        private async void SendMessage()
        {
            string text = _input.Text.Trim();
            if (text.Length == 0) return;

            AiSettings settings = _getSettings();
            if (settings == null || !settings.Enabled)
            {
                AppendBlock("[AI is disabled. Enable it in AI → Settings.]");
                return;
            }

            // Agent mode: route to the editor's previewed-edit pipeline instead of chat.
            if (_agentMode.Checked && _runAgent != null)
            {
                _input.Clear();
                AppendBlock("You (agent): " + text);
                _send.Enabled = false;
                _input.Enabled = false;
                try
                {
                    string status = await _runAgent(text);
                    AppendBlock("AI: " + status);
                }
                catch (Exception ex)
                {
                    AppendBlock("[Error: " + ex.Message + "]");
                }
                finally
                {
                    _send.Enabled = true;
                    _input.Enabled = true;
                    _input.Focus();
                }
                return;
            }

            _input.Clear();
            AppendBlock("You: " + text);
            _history.Add(new AiMessage("user", text));

            string system = "You are a helpful assistant embedded in the n+ text editor.";
            if (_attachDoc.Checked)
            {
                string doc = _getDocText?.Invoke() ?? "";
                if (doc.Length > 0)
                    system += "\n\nThe user's current document is:\n\n" + doc;
            }

            _send.Enabled = false;
            _input.Enabled = false;
            StartAiBlock();   // writes "AI: "; streamed tokens are appended inline
            try
            {
                // onToken runs on the UI thread (no ConfigureAwait(false) downstream).
                string full = await _client.CompleteStreamAsync(
                    settings, _history, system, chunk => AppendInline(chunk), CancellationToken.None);

                if (string.IsNullOrEmpty(full)) AppendInline("(no response)");
                _history.Add(new AiMessage("assistant", full ?? ""));
            }
            catch (Exception ex)
            {
                // Drop the failed user turn so the history doesn't break alternation on retry.
                if (_history.Count > 0 && _history[_history.Count - 1].Role == "user")
                    _history.RemoveAt(_history.Count - 1);
                AppendInline("\n[Error: " + ex.Message + "]");
            }
            finally
            {
                _send.Enabled = true;
                _input.Enabled = true;
                _input.Focus();
            }
        }

        // A blocked message (the user turn, or a status line) with a blank-line gap before it.
        private void AppendBlock(string line)
        {
            if (_output.TextLength > 0) _output.AppendText(Environment.NewLine + Environment.NewLine);
            _output.AppendText(line);
            ScrollToEnd();
        }

        // Opens the assistant's reply block; streamed chunks are appended after it.
        private void StartAiBlock()
        {
            if (_output.TextLength > 0) _output.AppendText(Environment.NewLine + Environment.NewLine);
            _output.AppendText("AI: ");
            ScrollToEnd();
        }

        // Appends a streamed token to the end of the current line.
        private void AppendInline(string chunk)
        {
            _output.AppendText(chunk);
            ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            _output.SelectionStart = _output.TextLength;
            _output.ScrollToCaret();
        }

        public void ApplyTheme(bool dark)
        {
            Color bg = dark ? Color.FromArgb(30, 30, 35) : SystemColors.Window;
            Color fg = dark ? Color.Gainsboro : SystemColors.WindowText;
            Color headerBg = dark ? Color.FromArgb(40, 40, 45) : SystemColors.Control;

            BackColor = bg;
            _output.BackColor = bg; _output.ForeColor = fg;
            _input.BackColor = bg; _input.ForeColor = fg;
            _header.BackColor = headerBg; _header.ForeColor = dark ? Color.Gainsboro : Color.Black;
            if (_header.Parent != null) _header.Parent.BackColor = headerBg;
            _attachDoc.BackColor = headerBg; _attachDoc.ForeColor = dark ? Color.Gainsboro : Color.Black;
            _agentMode.BackColor = headerBg; _agentMode.ForeColor = dark ? Color.Gainsboro : Color.Black;
            _closeBtn.BackColor = headerBg; _closeBtn.ForeColor = dark ? Color.Gainsboro : Color.Black;
        }
    }
}
