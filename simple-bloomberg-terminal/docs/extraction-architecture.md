# Arhitektura izdvajanja podataka iz dokumenata pomoću LLM-a

## 1. Opći dizajn izdvajanja

Sustav je oblikovan kao zajednički proces koji jedan SEC dokument pretvara u kratak i upotrebljiv skup nalaza. Isti proces koriste dva potrošača: razgovor s korisnikom i mjerenje kvalitete izdvajanja. Time se izbjegava izrada dviju različitih implementacija za isti problem.

Proces se može sažeti u pet koraka:

1. Dohvaća se izvorni dokument iz sustava SEC EDGAR.
2. Iz dokumenta se odabiru dijelovi koji su važni za troškove, prihode ili rizike.
3. Odabrani tekst dijeli se na manje cjeline koje je lakše poslati jezičnom modelu.
4. Više brzih LLM radnika paralelno pregledava te cjeline i pronalazi moguće podatke s dokaznim tekstom.
5. Nalazi se spajaju u zajednički kontekst koji koristi vodeći LLM. U razgovoru on odgovara korisniku, a u mjerenju vraća strukturirani rezultat za analizu kvalitete.

Važno je razlikovati dvije uloge modela. Brzi radnici traže informacije u manjim dijelovima dokumenta, dok vodeći model povezuje njihove nalaze i oblikuje konačan odgovor. Takav pristup smanjuje količinu teksta koju jedan poziv mora obraditi i omogućuje da se isti rezultat skeniranja primijeni u različitim dijelovima aplikacije.

## 2. Dohvat izvornog dokumenta

### Zašto ovaj dio postoji?

Izdvajanje mora započeti pouzdanim izvornim tekstom. U ovoj aplikaciji izvor je primarni dokument financijske prijave spremljen u arhivi SEC EDGAR. Servis za dohvat skriva detalje izrade URL-a i HTTP komunikacije od ostatka procesa.

### Što radi?

Metoda prima CIK poduzeća, pristupni broj prijave i naziv dokumenta. Na temelju njih stvara adresu dokumenta, dohvaća sadržaj i vraća ga kao tekst. Ako dokument ne postoji, vraća praznu vrijednost umjesto sadržaja.

**Datoteka:** `Services/Clients/Edgar/StockApiClient.cs`

**Naslov isječka: Dohvat primarnog dokumenta iz SEC EDGAR arhive**

```csharp
public async Task<string?> GetFilingDocument(
    string cik, string accessionNoDashes, string primaryDocument)
{
    var url = $"https://www.sec.gov/Archives/edgar/data/{cik}/{accessionNoDashes}/{primaryDocument}";
    var resp = await _http.GetAsync(url);
    if (resp.StatusCode == HttpStatusCode.NotFound) return null;
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadAsStringAsync();
}
```

## 3. Odabir važnih dijelova dokumenta

### Zašto ovaj dio postoji?

Financijske prijave mogu biti vrlo duge, a svi njihovi dijelovi nisu jednako važni za svaku vrstu izdvajanja. Slanje cijelog dokumenta modelu povećalo bi vrijeme i trošak obrade te bi u odgovor unijelo nepotreban tekst. Zato se za svaku domenu unaprijed određuju relevantne SEC stavke.

### Što radi?

Za rizike se pregledavaju stavke 1A i 7A. Za troškove se pregledavaju stavke 1, 7 i 8, dok se za prihode koriste stavke 1, 1A, 7 i 8. Nakon odabira, tekst se dijeli na cjeline ograničene veličine. Postoji i gornja granica ukupnog broja cjelina kako jedan neobično velik dokument ne bi stvorio neograničen broj LLM poziva.

**Datoteka:** `Services/Extraction/FilingSections.cs`

**Naslov isječka: Odabir SEC stavki prema vrsti izdvajanja**

```csharp
public static string[] ItemsFor(ExtractionNode node) => node switch
{
    ExtractionNode.RISK => ["1A", "7A"],
    ExtractionNode.COST => ["1", "7", "8"],
    _ => ["1", "1A", "7", "8"],
};

public const int MaxChunkChars = 4000;
public const int MaxScanChunks = 48;
```

**Naslov isječka: Pretvaranje odabranih stavki u manje tekstne cjeline**

```csharp
public static List<FilingChunk> Build(string raw, string[] items)
{
    var text = ToText(raw);
    var chunks = new List<FilingChunk>();
    foreach (var item in items)
    {
        var body = SectionBody(text, item);
        if (body is null) continue;
        int n = 0;
        foreach (var chunk in Paragraphs(body))
        {
            chunks.Add(new FilingChunk($"Item {item}", chunk, $"Item {item}"));
            if (++n >= MaxChunksPerSection) break;
            if (chunks.Count >= MaxScanChunks) return chunks;
        }
    }
    return chunks;
}
```

## 4. Brza paralelna LLM ekstrakcija

### Zašto ovaj dio postoji?

Jedan LLM poziv nad cijelim dokumentom bio bi spor i teže bi zadržao pažnju na svim važnim detaljima. Brzi radnici dobivaju manje dijelove teksta pa svaki od njih ima jednostavan zadatak: pronaći kandidate i uz njih vratiti izvorni dokaz. Budući da su cjeline međusobno neovisne, mogu se obrađivati paralelno.

### Što radi?

Servis prvo izrađuje plan cjelina. Zatim za svaku cjelinu poziva brzi način rada LLM-a i traži JSON odgovor. Najviše šest radnika izvodi se istodobno. Rezultati svih radnika na kraju se spajaju, a kandidati istog naziva objedinjuju se u jedan nalaz.

**Datoteka:** `Services/Extraction/FastWorkerScanService.cs`

**Naslov isječka: Paralelno pokretanje brzih LLM radnika**

```csharp
private async Task<List<ExtractionSuggestion>> RunFastWorkerAgentsAsync(
    IReadOnlyList<FilingChunk> chunks, ExtractionNode node,
    Action<FastWorkerScanProgress>? onProgress,
    bool strictCounterparties, List<ExtractionSuggestion>? workerClaims,
    CancellationToken ct)
{
    using var gate = new SemaphoreSlim(MaxParallelFastWorkers);
    var perChunk = await Task.WhenAll(chunks.Select((c, i) =>
        RunFastWorkerAgentAsync(c, i, node, gate, onProgress, strictCounterparties, ct)));
    workerClaims?.AddRange(perChunk.SelectMany(claims => claims));

    var byName = new Dictionary<string, ExtractionSuggestion>(StringComparer.OrdinalIgnoreCase);
    foreach (var list in perChunk)
        foreach (var s in list)
        {
            if (string.IsNullOrWhiteSpace(s.Name)) continue;
            byName[s.Name] = byName.TryGetValue(s.Name, out var seen)
                ? MergeSuggestions(seen, s)
                : s;
        }
    return byName.Values.ToList();
}
```

**Naslov isječka: LLM obrada jedne tekstne cjeline**

```csharp
var system = FastWorkerPromptFor(node, strictCounterparties);
var prompt = $"Section: {chunk.Section}\n\nExcerpt:\n\"\"\"\n{chunk.Text}\n\"\"\"";

var completion = await _llm.CompleteAsync(
    new ChatRequest(
        system,
        prompt,
        FastWorkerMaxTokens,
        JsonObject: true,
        Fast: true),
    ct);

var found = ParseFastWorkerResponse(
    completion.Content, chunk.Section, node).ToList();
```

## 5. Zajednički kontekst za vodeći model

### Zašto ovaj dio postoji?

Razgovor i mjerenje trebaju dobiti podatke u istom obliku. Zajednički servis za kontekst sprječava da svaki potrošač zasebno dohvaća dokument, pokreće skeniranje i priprema tekst. On je središnja veza između brzih radnika i vodećeg modela.

### Što radi?

Servis koristi rezultat brzih radnika koji mu je izravno predan ili pokreće novo skeniranje. Rezultati LLM radnika ne čitaju se iz predmemorije. Ako skeniranje ne pronađe nijedan nalaz, servis vraća prazan kontekst. Vodeći model tako dobiva isključivo rezultat brzih radnika iz trenutačnog izvođenja.

**Datoteka:** `Services/Extraction/FilingAnalysisContextService.cs`

**Programski kod 5. Izgradnja zajedničkog konteksta iz rezultata trenutačnog skeniranja**

```csharp
public async Task<string> BuildAsync(
    long companyId,
    string accession,
    string doc,
    ExtractionNode node,
    bool scanIfMissing = true,
    string? fastWorkerDigest = null,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(accession) ||
        string.IsNullOrWhiteSpace(doc))
        return "";

    var digest = fastWorkerDigest is not null
        ? fastWorkerDigest
        : scanIfMissing
            ? await _fastWorkerScan.CreateFastWorkerDigestAsync(
                companyId,
                accession,
                doc,
                node,
                ct)
            : "";

    return string.IsNullOrEmpty(digest)
        ? ""
        : "\n\n" + digest;
}
```

## 6. Vodeći LLM kao zajednički završni korak

### Zašto ovaj dio postoji?

Nalazi brzih radnika još nisu konačan odgovor. Potrebno ih je staviti u kontekst korisničkog pitanja ili ih pretvoriti u jedinstven strukturirani zapis. Tu ulogu ima vodeći LLM. Isti servis podržava dva načina rada jer razgovor i mjerenje imaju različite potrebe.

### Što radi?

Za mjerenje koristi potpuni odgovor: aplikacija čeka da cijeli rezultat bude gotov kako bi ga mogla parsirati. Za razgovor koristi prijenos odgovora u dijelovima, pa korisnik tekst vidi čim ga model počne stvarati. Kod potpunog odgovora postoji i jedno ponovno pokušavanje u slučaju privremene komunikacijske pogreške.

**Datoteka:** `Services/Extraction/LeadAgentRunner.cs`

**Naslov isječka: Potpuni i strujani poziv vodećeg LLM-a**

```csharp
public async Task<LlmCompletion> CompleteAsync(
    string systemPrompt, string filingContext, string userPrompt,
    int maxTokens, CancellationToken ct = default)
{
    var request = new ChatRequest(
        systemPrompt + filingContext,
        userPrompt,
        MaxTokens: maxTokens);

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            return await llm.CompleteAsync(request, ct);
        }
        catch (Exception ex) when (
            attempt < MaxCompletionAttempts &&
            !ct.IsCancellationRequested &&
            ex is HttpRequestException or IOException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "Lead-agent completion transport failed on attempt {Attempt}/{MaxAttempts}; retrying",
                attempt,
                MaxCompletionAttempts);
        }
    }
}

public async IAsyncEnumerable<ChatDelta> StreamAsync(
    string systemPrompt, string filingContext,
    IReadOnlyList<LlmMessage> messages,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var request = new List<LlmMessage>
    {
        new("system", systemPrompt + filingContext)
    };
    request.AddRange(messages);

    await foreach (var delta in llm.StreamAsync(request, ct: ct))
        yield return delta;
}
```

## 7. Potrošač 1: razgovor s korisnikom

### Zašto ovaj dio postoji?

Razgovor omogućuje korisniku da prirodnim jezikom pregleda pronađene dobavljače, kupce ili rizike. Korisnik ne mora sam prolaziti kroz cijelu financijsku prijavu, već može postavljati dodatna pitanja i odlučiti koje podatke želi zadržati.

### Što radi?

Servis koristi rezultat skeniranja koji mu je izravno predan ili pokreće novo skeniranje. Kada mora pokrenuti brze LLM radnike, korisniku šalje status da je skeniranje u tijeku. Zatim priprema zajednički kontekst, pretvara povijest razgovora u LLM poruke i šalje ih vodećem modelu. Odgovor se vraća postupno radi boljeg korisničkog iskustva.

**Datoteka:** `Services/Extraction/Chat/ExtractionChatService.cs`

**Naslov isječka: Korištenje zajedničke ekstrakcije u razgovoru**

```csharp
public async IAsyncEnumerable<ChatDelta> StreamReplyAsync(
    long companyId, string accession, string doc, ExtractionNode node,
    IReadOnlyList<ChatMessage> history,
    string? fastWorkerDigest = null,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var hasFiling = !string.IsNullOrWhiteSpace(accession) &&
                    !string.IsNullOrWhiteSpace(doc);
    if (hasFiling && fastWorkerDigest is null)
        yield return new ChatDelta(
            "status",
            "Scanning the filing with parallel fast worker agents...");

    var filingContext = await _context.BuildAsync(
        companyId, accession, doc, node,
        scanIfMissing: fastWorkerDigest is null,
        fastWorkerDigest: fastWorkerDigest,
        ct: ct);

    var messages = history
        .Select(message => new LlmMessage(
            message.Role == "assistant" ? "assistant" : "user",
            message.Content))
        .ToList();

    await foreach (var delta in _leadAgent.StreamAsync(
        LeadAgentPromptFor(node), filingContext, messages, ct))
        yield return delta;
}
```

## 8. Potrošač 2: mjerenje ekstrakcije

### Zašto ovaj dio postoji?

Jedan uspješan odgovor nije dovoljan za procjenu kvalitete LLM ekstrakcije. Model može u različitim izvođenjima vratiti malo drukčije rezultate. Mjerni dio zato više puta pokreće isti proces i bilježi pronađene tvrdnje, pogreške i dijelove dokumenta iz kojih su podaci došli.

### Što radi?

Mjerenje trenutačno obrađuje troškovne protustranke, odnosno dobavljače. Prvo izvođenje zagrijava predmemoriju dokumenta i parsiranih dijelova. Ostala izvođenja mogu se pokrenuti paralelno, ali svako ima vlastito brzo skeniranje i vlastiti završni poziv vodećem modelu. Na kraju kalkulator uspoređuje rezultate svih izvođenja.

**Datoteka:** `Services/Extraction/Measurement/CounterpartyMeasurementService.cs`

**Naslov isječka: Ponavljanje zajedničkog procesa radi mjerenja**

```csharp
var first = await ExecuteRunAsync(
    target, node, strictCounterparties, 1, null,
    keys, model, onProgress, ct);

using var gate = new SemaphoreSlim(MaxParallelRuns);
var rest = await Task.WhenAll(
    Enumerable.Range(2, Math.Max(0, runs - 1)).Select(run => ExecuteRunAsync(
        target, node, strictCounterparties, run, gate,
        keys, model, onProgress, ct)));

return MeasurementCalculator.Calculate(
    rest.Prepend(first).ToArray(), model, runAt);
```

**Naslov isječka: Jedan ciklus brzih radnika i vodećeg modela**

```csharp
var scanned = await fastWorkerScan.RunFastWorkerScanAsync(
    target.CompanyId,
    target.Accession,
    target.Document,
    node,
    strictCounterparties: strictCounterparties,
    captureArtifacts: true,
    ct: ct);

var filingContext = await context.BuildAsync(
    target.CompanyId,
    target.Accession,
    target.Document,
    node,
    scanIfMissing: false,
    fastWorkerDigest: scanned.FastWorkerDigest,
    ct: ct);

var completion = await leadAgent.CompleteAsync(
    MeasurementPrompts.LeadAgentSystemPrompt,
    filingContext,
    MeasurementPrompts.LeadAgentUserPrompt,
    LeadAgentMaxTokens,
    ct);
```

## 9. Predmemorija i ponovno korištenje podataka

### Zašto ovaj dio postoji?

AI chat i measurement mogu više puta koristiti isti SEC dokument. Kada je dokument već dohvaćen i parsiran, nema potrebe ponovno preuzimati isti sadržaj i ponovno pronalaziti njegovu strukturu. Predmemorija privremeno čuva samo te determinističke podatke i tako smanjuje nepotreban rad bez utjecaja na rezultat LLM ekstrakcije.

### Što radi?

U predmemoriji se čuvaju izvorni SEC dokument i pronađeni podnaslovi unutar relevantnih dijelova dokumenta. To su deterministički podaci: za isti dokument njihov sadržaj ostaje jednak. Rezultati brzih LLM radnika ne spremaju se u predmemoriju. Dokument i podnaslovi čuvaju se 30 minuta.

### Kako AI chat koristi predmemoriju?

Pri svakoj poruci AI chat ponovno pokreće brze LLM radnike i vodeći model. Brzi radnici ponovno koriste dokument i podnaslove iz predmemorije, ali stvaraju novi rezultat za trenutačno izvođenje. Taj se rezultat izravno predaje vodećem modelu i ne sprema se za sljedeću poruku.

Povijest razgovora nije spremljena u ovoj predmemoriji. Ona se vodi odvojeno i šalje vodećem modelu pri svakom novom zahtjevu.

**Datoteka:** `Services/Extraction/FastWorkerScanService.cs`

**Programski kod 9. Predmemorija determinističkih podataka dokumenta**

```csharp
private static string HeadingsKey(
    string accession, string doc, ExtractionNode node) =>
    $"filing-headings:{node}:{accession}:{doc}";

public static string RawKey(string accession, string doc) =>
    $"filing-raw:{accession}:{doc}";

private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);
```

### Kako measurement koristi istu predmemoriju?

Predmemorija kod measurementa ima istu svrhu: spriječiti ponovno dohvaćanje i parsiranje istog dokumenta. Measurement najprije zasebno izvršava prvi run. Tijekom tog runa dokument i pronađeni podnaslovi spremaju se u predmemoriju. Nakon završetka prvog runa ostali runovi pokreću se paralelno i koriste iste determinističke podatke dokumenta.

Svaki measurement run ponovno pokreće brze LLM radnike i vodeći model. Nijedan run ne koristi LLM nalaze prethodnog runa. To je potrebno jer measurement uspoređuje više neovisnih LLM izvođenja. Iz predmemorije se ponovno koriste samo dokument i rezultati njegova parsiranja.

## 10. Sažetak odgovornosti

Cijela arhitektura temelji se na jasnoj podjeli odgovornosti. `StockApiClient` dohvaća dokument. `FilingSections` izdvaja važne stavke i dijeli tekst. `FastWorkerScanService` organizira brzu paralelnu LLM pretragu. `FilingAnalysisContextService` priprema zajednički kontekst, a `LeadAgentRunner` izvršava završni LLM poziv. Na kraju `ExtractionChatService` taj proces prilagođava razgovoru, dok ga `CounterpartyMeasurementService` ponavlja i šalje na izračun mjernih rezultata.

Najvažnija ideja dizajna jest da razgovor i mjerenje nisu dvije zasebne ekstrakcije. Oni su dva potrošača istog osnovnog procesa. Zbog toga se promjene u dohvaćanju, podjeli dokumenta ili radu brzih LLM radnika mogu napraviti na jednom mjestu, a oba potrošača automatski dobivaju isto ponašanje.

## 11. Izlazni entitetski modeli

Nakon ekstrakcije oba potrošača pretvaraju LLM rezultat u unaprijed definiran model. Time se slobodno generirani tekst povezuje s ostatkom aplikacije. AI chat koristi model prilagođen korisničkoj potvrdi i spremanju podataka, dok measurement koristi model prilagođen prikazu i usporedbi rezultata više izvođenja.

### Model koji AI chat priprema za spremanje

AI chat uz običan odgovor može vratiti strukturirani `save` blok. Aplikacija taj blok pretvara u `SaveBatchItem`. Model sadrži naziv pronađenog zapisa, moguću klasifikaciju rizika, povezano poduzeće te dokaz iz dokumenta. Vrijednost i postotak nisu dio ovog modela jer je ekstrakcija usmjerena na prepoznavanje imenovanih protustranaka i njihova odnosa s poduzećem.

Korisnik prije spremanja odabire koje predložene zapise želi zadržati. LLM rezultat zato se ne sprema automatski. Tek nakon korisničke potvrde odabrani zapis povezuje se s izvornim SEC dokumentom i sprema u odgovarajući entitet prihoda, troška ili rizika.

**Datoteka:** `Models/ViewModels/ExtractionViewModels.cs`

**Programski kod 11.1. Model zapisa koji korisnik može potvrditi i spremiti**

```csharp
public class SaveBatchItem
{
    public string Name { get; set; } = string.Empty;
    public string? Classification { get; set; }
    public string? Note { get; set; }
    public string? RelatedCompany { get; set; }
    public string? RelatedCompanyTicker { get; set; }
    public string? Reference { get; set; }
    public string? Evidence { get; set; }
}
```

### Zašto je za spremanje potreban JSON?

Običan tekst koji vodeći model napiše u razgovoru nije dovoljan za spremanje. Uz korisniku razumljiv odgovor model mora vratiti i `save` blok koji sadrži ispravan JSON objekt. JSON svakom podatku daje poznato ime, primjerice `name`, `related_company` i `evidence`, pa aplikacija može pouzdano povezati podatke s poljima modela `SaveBatchItem`.

Klijentski kod najprije pronalazi sve blokove označene s `save`, a zatim njihov sadržaj obrađuje pomoću `JSON.parse`. Ako sadržaj nije ispravan JSON, blok se preskače i taj se prijedlog ne može prikazati korisniku za odabir niti poslati na spremanje. Nakon korisničke potvrde odabrani objekti ponovno se pretvaraju u JSON i šalju endpointu `/extraction/save-batch`.

**Datoteka:** `wwwroot/js/site.js`

**Programski kod 11.2. Čitanje JSON zapisa iz odgovora AI chata**

````javascript
function parseSaves(id) {
    const byName = new Map();
    const re = /```save\s*([\s\S]*?)```/g;
    for (const m of read(chatKey(id), [])) {
        if (m.role !== 'assistant') continue;
        let x; re.lastIndex = 0;
        while ((x = re.exec(m.content)) !== null) {
            let j;
            try {
                j = JSON.parse(x[1].trim());
            } catch {
                continue;
            }
            const s = normalizeSave(j);
            if (s.name) {
                s.key = s.name;
                byName.set(s.name, s);
            }
        }
    }
    return [...byName.values()];
}
````

### Model rezultata measurementa

Measurement ne stvara zapis koji korisnik sprema u bazu podataka. Njegov je izlaz `CounterpartyMeasurementResult`, koji objedinjuje sirove retke svih runova nad jednim dokumentom. Model sadrži osnovne podatke o dokumentu i modelu, broj izvođenja, ukupan broj pogrešaka i pronađene retke.

Iz tih redaka korisničko sučelje grupira protustranke, prikazuje neslaganja između brzih radnika i vodećeg modela te računa jednostavan omjer pojavljivanja `x/ukupan broj runova`. Model ne sprema unaprijed izračunate postotke ponovljivosti, prisutnosti dokaza ili zadržavanja nalaza.

**Datoteka:** `Services/Extraction/Measurement/CounterpartyModels.cs`

**Programski kod 11.3. Model objedinjenog rezultata mjerenja**

```csharp
public sealed record CounterpartyMeasurementResult(
    string Company,
    string Cik,
    string Accession,
    int Runs,
    int TotalErrors,
    string Model,
    DateTime RunAt,
    IReadOnlyList<CounterpartyMeasurementRow> Rows,
    string? Error = null);
```
