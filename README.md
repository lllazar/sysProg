# sysProg — Proxy Server

## Opis projekta

Konzolni serverski program napisan u C# koji prima HTTP zahteve od klijenata,
pribavlja URL-ove slika iz Rijksmuseum API-ja i kešira rezultate radi bržeg odgovora.
Sistem podržava istovremeni rad sa većim brojem zahteva korišćenjem ThreadPool-a
i blokirajuće sinhronizacije.

## Ukratko rad Rijksmusem API-ja

Za prosledjen Query vraća se Json file koji u okviru property-ja Ordered Items i id vraca linkove oblika 
https://id.rijksmuseum.nl/{ID}. Unosom ovog linka se vrši redirekcija do linka oblika https://data.rijksmuseum.nl/{ID} i potom
još jedna redirekcija do linka web stranice na kojoj se nalazi slika.

## Arhitektura sistema

Sistem je zasnovan na razdvajanju prijema i obrade zahteva:

- **Main nit** — prima HTTP zahteve putem `HttpListener.GetContext()` i smešta ih u deljeni red (`Queue`)
- **ProcessingThread niti** — uzimaju zahteve iz reda, pribavljaju podatke iz API-ja i vraćaju odgovor klijentu
- **ShutdownThread** — osluškuje konzolni unos, gasi server pri unosu `67`
- **ClearingThread** — periodično čisti keš svakih 10 sekundi

Broj paralelnih niti je ograničen na 20 putem `ThreadPool.SetMaxThreads(20, 20)`.
Za pribavljanje URL-ova slika po ID-ju koristi se dodatni `SemaphoreSlim(10)`
koji ograničava broj istovremenih niti na 10 po zahtevu.

## Kritične sekcije i sinhronizacija

### Red zahteva (Queue)

`Queue<HttpListenerContext>` je deljena struktura između Main niti i ProcessingThread niti.
Nije thread-safe sama po sebi, pa je svaki pristup zaštićen `lock(lockObj)` blokom.

- Main nit dodaje zahtev u red i poziva `Monitor.Pulse` da probudi nit koja čeka
- ProcessingThread čeka u `while` petlji uz `Monitor.Wait` dok red nije prazan
- `while` umesto `if` štiti od lažnih buđenja

### Keš (Cache — LRU strategija)

Keš koristi `ConcurrentDictionary` za thread-safe čitanje i pisanje vrednosti,
i `LinkedList` za praćenje LRU redosleda. Pošto `LinkedList` nije thread-safe,
svaki pristup listi je zaštićen `lock(lockObject)`.

LRU logika:
- svaki pristup postojećem ključu pomera ga na kraj liste (`Touch`)
- kada je keš pun, briše se element sa početka liste (najduže nekorišćen)
- novi element se dodaje na kraj liste

### Cache Stampede zaštita

Kada više niti istovremeno zatraži isti ID, HTTP poziv treba da se izvrši
samo jednom. Rešenje je per-ID lokovanje putem `ConcurrentDictionary<long, object>`:

```csharp
object locker = locks.GetOrAdd(id, _ => new object());
lock (locker)
{
    // double-check — druga nit koja je čekala uzima iz keša
    picURL = cache.Get(id);
    if (!string.IsNullOrEmpty(picURL)) return picURL;

    // samo jedna nit dolazi do ovde i izvršava HTTP poziv
    ...
    cache.Add(id, picURL);
}
```

`finally` blok garantuje brisanje lokera iz rečnika čak i u slučaju izuzetka.

### Logger

`Logger` koristi `ConcurrentQueue<string>` za thread-safe baferovanje poruka.
`lock(locker)` blok štiti `StreamWriter` pri ispisu na disk ili konzolu,
kao i pri promeni izlaznog toka (`ChangeLogFile`).

## Ponašanje pri različitim nivoima opterećenja

### Mali broj zahteva (< 20 istovremenih)

Svaki zahtev dobija svoju ThreadPool nit odmah.
`Monitor.Wait` se retko aktivira jer nit pronađe zahtev u redu čim se pokrene.

### Veliki broj zahteva (>= 20 istovremenih)

ThreadPool dostiže maksimum od 20 niti.
Nove niti čekaju dok se neka ne oslobodi.
`Monitor.Wait/Pulse` mehanizam postaje ključan —
niti se blokiraju dok ne dobiju signal da ima posla u redu,
čime se izbegava aktivno čekanje (busy waiting).


## Korišćeni mehanizmi sinhronizacije

| Mehanizam | Gde se koristi | Zašto |
|-----------|---------------|-------|
| `lock` | Queue, Cache, Logger, urls lista | Zaštita kritičnih sekcija koje nisu thread-safe |
| `Monitor.Wait/Pulse` | ProcessingThread / Main | Blokirajuće čekanje na zahteve bez busy waitinga |
| `ConcurrentDictionary` | Cache, per-ID lokovi | Thread-safe čitanje/pisanje bez dodatnog locka |
| `SemaphoreSlim` | Kreiranje niti po ID-ju | Ograničava max 10 istovremenih niti po zahtevu |
| `volatile` | bool end | Garantuje vidljivost promene između niti bez locka |

## Pokretanje

```bash
dotnet run
```

Server sluša na `http://localhost:5000/`.  

