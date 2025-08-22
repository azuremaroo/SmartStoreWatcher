using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using HtmlAgilityPack;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SmartStoreWatcher.Utils;

namespace SmartStoreWatcher
{
    public partial class MainWindow : Window
    {
        private readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        });

        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private readonly string _stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartStoreWatcher", "state");
        private readonly string _configFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartStoreWatcher", "config.json");
        private readonly Regex _productIdRegex = new(@"smartstore\.naver\.com\/[^\/\s]+\/products\/(\d+)", RegexOptions.Compiled);
        private ToastNotifierCompat? _notifier;
        private List<StoreWatcher> _watchers = new();
        private bool _useWebView = false;
        private TimeSpan _interval = TimeSpan.FromMinutes(3);

        public MainWindow()
        {
            InitializeComponent();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 ...");
            _timer.Interval = TimeSpan.FromSeconds(20);
            _timer.Tick += async (s, e) => await PollAll();

            LoadConfigUI(); // ★ 추가: 앱 시작 시 마지막 설정 불러오기
        }

        private sealed class StoreEntry { public string? Name { get; set; } public string Url { get; set; } = ""; }
        private sealed class AppConfig
        {
            public StoreEntry[]? Stores { get; set; }   // 새 포맷
            public string[]? Urls { get; set; }         // 구버전 호환
            public int IntervalMinutes { get; set; }
            public bool UseWebView { get; set; }
        }

        private sealed class StoreWatcher
        {
            public StoreWatcher(string url, string slug, string name)
            { Url = url; Slug = slug; Name = name ?? ""; }

            public string Url { get; }
            public string Slug { get; }
            public string Name { get; }
            public string Display => string.IsNullOrWhiteSpace(Name) ? Slug : Name;

            public HashSet<string> KnownIds { get; set; } = new(StringComparer.Ordinal);
            public bool SuppressFirstToast { get; set; } = true;
            public DateTime LastRun { get; set; } = DateTime.MinValue;
        }

        private static (string? name, string url)? ParseStoreLine(string line)
        {
            var s = line.Trim();
            if (string.IsNullOrEmpty(s)) return null;

            int sep = s.IndexOfAny(new[] { '|', '\t', ',' });
            string? name = null; string url;
            if (sep >= 0) { name = s[..sep].Trim(); url = s[(sep + 1)..].Trim(); }
            else url = s;

            if (!url.Contains("smartstore.naver.com", StringComparison.OrdinalIgnoreCase)) return null;
            return (string.IsNullOrWhiteSpace(name) ? null : name, url);
        }

        private void LoadConfigUI()
        {
            try
            {
                if (!File.Exists(_configFile)) return;
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configFile));
                if (cfg == null) return;

                string[] lines;
                if (cfg.Stores != null && cfg.Stores.Length > 0)
                    lines = cfg.Stores.Select(s => string.IsNullOrWhiteSpace(s.Name) ? s.Url : $"{s.Name} | {s.Url}").ToArray();
                else
                    lines = (cfg.Urls ?? Array.Empty<string>());

                UrlsBox.Text = string.Join(Environment.NewLine, lines);
                IntervalBox.Text = (cfg.IntervalMinutes > 0 ? cfg.IntervalMinutes : 3).ToString();
                UseWebViewCheck.IsChecked = cfg.UseWebView;
            }
            catch { }
        }

        private void SaveConfigUI(StoreEntry[] entries)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configFile)!);
                var cfg = new AppConfig
                {
                    Stores = entries,
                    IntervalMinutes = (int)_interval.TotalMinutes,
                    UseWebView = _useWebView
                };
                File.WriteAllText(_configFile, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }


        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_watchers.Count > 0) return;

            if (!int.TryParse(IntervalBox.Text.Trim(), out var minutes) || minutes < 1) minutes = 3;
            _interval = TimeSpan.FromMinutes(minutes);
            _useWebView = UseWebViewCheck.IsChecked == true;

            var entries = UrlsBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseStoreLine).Where(x => x != null)
                .Select(x => new StoreEntry { Name = x!.Value.name, Url = x!.Value.url })
                .GroupBy(e => e.Url, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .ToArray();

            if (entries.Length == 0)
            {
                MessageBox.Show("한 줄에 '이름 | URL' 형식으로 입력하세요. 이름은 선택사항입니다.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Directory.CreateDirectory(_stateDir);

            foreach (var e2 in entries)
            {
                var w = new StoreWatcher(e2.Url, Slugify(e2.Url), e2.Name ?? "");
                var existed = LoadState(w);
                w.SuppressFirstToast = !existed;
                _watchers.Add(w);
            }

            if (_useWebView) await EnsureWebViewReady();

            SaveConfigUI(entries);        // ★ 입력 저장
            StatusText.Text = $"워처 {_watchers.Count}개, 간격 {minutes}분";
            Log($"시작. {_watchers.Count}개 등록");

            await PollAll();
            _timer.Start();

            StartBtn.IsEnabled = false; StopBtn.IsEnabled = true;
            UrlsBox.IsEnabled = false; IntervalBox.IsEnabled = false; UseWebViewCheck.IsEnabled = false;
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _watchers.Clear();

            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
            UrlsBox.IsEnabled = true;
            IntervalBox.IsEnabled = true;
            UseWebViewCheck.IsEnabled = true;

            StatusText.Text = "중지됨";
            Log("중지");
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            var entries = UrlsBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseStoreLine).Where(x => x != null)
                .Select(x => new StoreEntry { Name = x!.Value.name, Url = x!.Value.url })
                .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .ToArray();

            if (int.TryParse(IntervalBox.Text.Trim(), out var minutes) && minutes >= 1)
                _interval = TimeSpan.FromMinutes(minutes);
            _useWebView = UseWebViewCheck.IsChecked == true;

            SaveConfigUI(entries);
            base.OnClosing(e);
        }

        private async Task PollAll()
        {
            var now = DateTime.UtcNow;

            foreach (var w in _watchers)
            {
                if (now - w.LastRun < _interval) continue;
                w.LastRun = now;

                try
                {
                    var products = await FetchProducts(w.Url, _useWebView ? HiddenWebView : null);
                    if (products.Count == 0)
                    {
                        Log($"[{w.Display}] 제품을 찾지 못함");
                        continue;
                    }

                    if (w.SuppressFirstToast)
                    {
                        foreach (var id in products.Select(p => p.Id)) w.KnownIds.Add(id);
                        SaveState(w);
                        w.SuppressFirstToast = false;
                        Log($"[{w.Display}] 초기 시드 완료 {w.KnownIds.Count}건. 알림 생략");
                        continue;
                    }

                    var newOnes = products.Where(p => !w.KnownIds.Contains(p.Id)).ToList();
                    if (newOnes.Count > 0)
                    {
                        foreach (var p in newOnes)
                        {
                            ShowToast(w, p);
                            Log($"[{w.Display}] 새 상품: {p.Title} ({p.Id})");
                        }
                        foreach (var id in newOnes.Select(p => p.Id)) w.KnownIds.Add(id);
                        SaveState(w);
                    }
                    else
                    {
                        Log($"[{w.Display}] 변화 없음. 스캔 {products.Count}개");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[{w.Display}] 오류: {ex.Message}");
                }
            }
        }

        private async Task EnsureWebViewReady()
        {
            try
            {
                if (HiddenWebView.CoreWebView2 != null) return;
                var env = await CoreWebView2Environment.CreateAsync();
                await HiddenWebView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                Log("WebView2 초기화 실패: " + ex.Message);
            }
        }

        private async Task<List<ProductItem>> FetchProducts(string url, WebView2? webview)
        {
            // 1) 정적 HTML 시도
            try
            {
                var html = await _http.GetStringAsync(url);
                var list = ParseProductLinks(html, url);
                if (list.Count > 0) return list;
            }
            catch { }

            // 2) 렌더링 모드
            if (webview != null && webview.CoreWebView2 != null)
            {
                try
                {
                    webview.Source = new Uri(url);
                    await webview.EnsureCoreWebView2Async();
                    await Task.Delay(2000);
                    for (int i = 0; i < 4; i++)
                    {
                        await webview.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0, document.body.scrollHeight);");
                        await Task.Delay(700);
                    }
                    string js = @"
                        (function(){
                            const anchors = Array.from(document.querySelectorAll('a[href*=""/products/""]'));
                            const seen = new Set();
                            const items = [];
                            for(const a of anchors){
                                let href = a.href || '';
                                if(!href) continue;
                                const m = href.match(/smartstore\.naver\.com\/[^\/]+\/products\/(\d+)/);
                                if(!m) continue;
                                const id = m[1];
                                if(seen.has(id)) continue;
                                seen.add(id);
                                const text = (a.innerText || a.getAttribute('title') || '').trim();
                                items.push({ id, href, title: text || ('상품 ' + id) });
                            }
                            return JSON.stringify(items);
                        })();";
                    var result = await webview.CoreWebView2.ExecuteScriptAsync(js);
                    if (result.StartsWith("\"") && result.EndsWith("\""))
                        result = JsonSerializer.Deserialize<string>(result) ?? "[]";
                    var dtos = JsonSerializer.Deserialize<List<ProductDto>>(result) ?? new();
                    return dtos.Select(x => new ProductItem { Id = x.id, Title = x.title ?? $"상품 {x.id}", Url = x.href }).ToList();
                }
                catch { }
            }

            return new List<ProductItem>();
        }

        private List<ProductItem> ParseProductLinks(string html, string baseUrl)
        {
            var list = new List<ProductItem>();
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href,'/products/')]");
                if (anchors == null) return list;

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var a in anchors)
                {
                    var href = a.GetAttributeValue("href", null);
                    if (string.IsNullOrWhiteSpace(href)) continue;

                    if (href.StartsWith("/"))
                    {
                        var uri = new Uri(baseUrl);
                        href = $"{uri.Scheme}://{uri.Host}{href}";
                    }

                    var m = _productIdRegex.Match(href);
                    if (!m.Success) continue;
                    var id = m.Groups[1].Value;
                    if (!seen.Add(id)) continue;

                    var title = a.InnerText?.Trim();
                    if (string.IsNullOrWhiteSpace(title)) title = $"상품 {id}";

                    list.Add(new ProductItem { Id = id, Title = title, Url = href });
                }
            }
            catch { }
            return list;
        }

        private void ShowToast(StoreWatcher w, ProductItem p)
        {
            new ToastContentBuilder()
                .AddText($"[{w.Display}] 새 상품")
                .AddText(p.Title)
                // 토스트 본문 클릭 시 스토어 열기
                .AddToastActivationInfo(w.Url, ToastActivationType.Protocol)
                .Show();
        }


        private bool LoadState(StoreWatcher w)
        {
            try
            {
                var file = Path.Combine(_stateDir, w.Slug + ".json");
                if (!File.Exists(file)) return false;
                var ids = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(file));
                if (ids != null) w.KnownIds = ids;
                return true;
            }
            catch { return false; }
        }

        private void SaveState(StoreWatcher w)
        {
            try
            {
                var file = Path.Combine(_stateDir, w.Slug + ".json");
                File.WriteAllText(file, JsonSerializer.Serialize(w.KnownIds, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private static string Slugify(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Select(ch => bad.Contains(ch) ? '_' : ch).ToArray());
            return cleaned.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
        }

        private void Log(string text)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
            LogList.Items.Insert(0, line);
        }

        private record ProductDto(string id, string href, string? title);
    }
}
