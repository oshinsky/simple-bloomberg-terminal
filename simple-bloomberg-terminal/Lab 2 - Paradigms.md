# Lab 2 – Paradigms

## Klijent-server komunikacija

- GET – šalje parametre via URL, prima HTML response
- POST – šalje parametre via form body na server
- URL dijelovi: protokol, domena, port, relativna putanja, URL parametri
- Zahtjev sadrži: URL, zaglavlje, POST vrijednosti, tip zahtjeva

---

## MVC paradigma

### Controller

- Klasa nasljeđuje `Controller`, sadrži **akcije** (metode)
- Akcija: naziv, async/sync, anotacija, povratni tip (`IActionResult`), parametri, `return View(model)`
- Anotacije: `[HttpGet]`, `[HttpPost]`, `[AllowAnonymous]`, itd.

### View

- `.cshtml` datoteka – kombinacija HTML-a i Razor C# naredbi
- `@model` direktiva na vrhu definira tip modela (strongly typed)
- `@Model.Svojstvo` ispisuje podatak iz modela
- `ViewData` – dictionary, nije strongly typed (lošije)
- Logika u view-u: samo `if` / `foreach` / TagHelperi – ništa kompleksnije

### Nomenklatura i konvencije

- Controller klasa: `XyzController` u folderu `Controllers/`
- View datoteke: `Views/Xyz/ActionName.cshtml`
- Automatsko mapiranje: akcija `About` → `Views/Home/About.cshtml`

### ViewModel

- Pomoćna klasa s prilagođenim podacima za view
- Razlika: Model = baza podataka; ViewModel = prilagođeni podaci za prikaz

### URL parametri akcije

- Query string parametri automatski se mapiraju na parametre akcije
- Primjer: `/Home/About?lang=hr` → `About(string lang)`

---

## URL usmjeravanje (Routing)

- Definirano u `Program.cs` via `app.MapControllerRoute(...)`
- Rute se procesiraju redom – aktivira se prva koja odgovara
- Defaultni pattern: `{controller=Home}/{action=Index}/{id?}`
- `Xyz` u URL-u → `XyzController` klasa

### URL rute – naredbe ActionLink i Url

- `@Html.ActionLink(...)` – generira `<a>` element
- `@Url.Action(...)` – generira string URL za `<a href="...">` ili JS
- `<a asp-controller="..." asp-action="...">` – Tag Helper (preporučeno u .NET Core)

---

## HTML osnove

### Container elementi

- `<html>`, `<head>`, `<body>` – struktura stranice
- `<div>` – blok element (zauzima cijelu liniju)
- `<span>` – inline element (zauzima samo potrebnu širinu)
- `<table>`, `<th>`, `<tr>`, `<td>` – tablični prikaz

### Elementi za unos vrijednosti

- `<form method="post">` – omotač za input elemente, šalje POST
- `<input type="text">`, `<input type="submit">`
- `<select>` – padajući izbornik
- `<textarea>` – višeredni unos teksta

---

## Twitter Bootstrap

### Grid system

- UI podijeljen u mrežu stupaca (12-stupčani grid)
- Automatski prilagodljivo (responsive) – mobiteli, tableti, desktop
- Klase: `container`, `row`, `col-md-X`, itd.

### Modal

- Popup prozor za prikaz informacija
- Može se otvoriti samo HTML atributima ili JavaScript funkcijama

---

## Razor sintaksa i view predlošci

- C# naredba počinje s `@`
- `@Model` – pristup proslijeđenom modelu (nakon `@model TipModela`)
- Unutar `{ }` bloka nije potreban `@` prefiks
- `@:` i `<text>` – ispis čistog teksta unutar C# bloka
- `@if (...)`, `@foreach (...)` – kontrola toka

### Index – lista elemenata

- Model: `List<T>` ili kolekcija
- Prikaz: `@foreach(var item in Model) { ... }` ili HTML `<table>`

### Details – pregled detalja

- Model: jedan entitet
- Prikazuje detaljne podatke jednog zapisa

---

## Mock repository i Dependency Injection

### Mock repository

- Klasa koja vraća statičke podatke umjesto prave baze
- Metode: `GetAll()`, `GetById(int id)`
- Imenovanje: `AuthorMockRepository`, `QuizMockRepository`
- Controller ne zna odakle podaci dolaze – samo ih traži

### Dependency Injection

- Ovisnosti se registriraju u `Program.cs`, ne instanciraju ručno s `new`
- `builder.Services.AddSingleton<T>()` – registracija
- Framework automatski prosljeđuje kroz konstruktor controllera
- Mock repository lako zamjenjiv pravim bez promjene controllera

---

## AI-asistirani razvoj i sub-agenti

## Model binding

### GET vs POST akcije

- **GET** – dohvat podataka za pregled, anotacija `[HttpGet]` (default)
- **POST** – obrada forme, anotacija `[HttpPost]`
- Isti naziv i URL mogu imati GET i POST varijantu akcije

### Pristup 1: FormCollection (najlošiji)

- Sve vrijednosti kao stringovi u dictionary
- Velika mogućnost greške u nazivima, ručna konverzija tipova

### Pristup 2: Jednostavni parametri (bolji)

- Parametri akcije direktno mapiraju `name` atribut HTML inputa
- Problem: broj parametara raste, greška u nazivu moguća

### Pristup 3: Model binding (najbolji)

- ASP.NET MVC automatski kreira instancu modela i puni polja prema `name` atributima
- Controller prima typed objekt: `Contact(ContactModel model)`

### Pristup 3b: Razor EditorFor (najtipskiji)

- `@Html.EditorFor(p => p.Ime, ...)` – strongly typed veza s poljem modela
- Koristi expression tree – greška u nazivu je compile-time, ne runtime
- Zahtijeva razumijevanje partial view koncepta
