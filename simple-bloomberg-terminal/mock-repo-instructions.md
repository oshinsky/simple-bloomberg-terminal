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