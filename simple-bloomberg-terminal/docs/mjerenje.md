# Mjerenje ekstrakcije protustranaka (COST)

## Što mjerenje obuhvaća

Mjere se dva sloja jezičnih modela, odvojeno: **brzi agenti-radnici**, koji čitaju pojedini isječak
izvješća, i **vodeći agent**, koji iz njihovih nalaza sastavlja konačnu tablicu. Jedan je prolaz uvijek
potpuni prolaz kroz proces — vlastito skeniranje radnicima i vlastiti poziv vodećem agentu.

**Ponovljivost.** Ista ekstrakcija istog izvješća pokreće se N puta. Stavka se ključa parom
`(smjer, normalizirani naziv protustranke)` i stabilna je ako se pojavila u svakom prolazu.
Istovjetnost iznosa bilježi se odvojeno. Varijacija u tekstu opisa bilježi se odvojeno i ne broji se
kao nestabilnost. Automatski.

**Utemeljenost isječaka.** Za svaku se stavku provjerava nalazi li se njezin isječak u izvornom tekstu
izvješća, nakon normalizacije bijelog prostora i uz prag podudaranja 0,90. Mjeri se odvojeno na izlazu
radnika i vodećeg agenta. Automatski.

**Preciznost.** Nad jednim prolazom svaka se stavka ručno označava kao ispravna, pogrešno
klasificirana, s pogrešnom vrijednošću, ili kao stavka koja uopće nije protustranka. Preciznost je
udio ispravnih. Ručno.

**Zadržavanje.** Udio nalaza radnika koji je vodeći agent prenio dalje. Pokazuje ono što utemeljenost
i preciznost ne vide — stavke koje su tiho ispuštene.

## Do kojih je promjena došlo

**1. COST ekstrakcija postala je ekstrakcija protustranaka.** Prije je tražila računovodstvenu
kategoriju (COGS / OPEX / TOTAL_COSTS) — prosudbu koju izvješće nikada ne navodi, pa je ništa u tekstu
ne može potkrijepiti. Jedinica je sada imenovana protustranka: naziv, smjer trgovanja, predmet
trgovanja, iznos i doslovni isječak. Svako je polje činjenica otisnuta u izvješću, dakle provjerljiva.

**2. Uklonjena je trijaža naslova jezičnim modelom.** Poziv modela prije je birao koje dijelove
izvješća skenirati, pa dva prolaza iste ekstrakcije nisu čitala isti tekst. Odabir je sada
determinističan i broj isječaka po prolazu konstantan, čime je mjerena varijanca svedena na ono što
modeli rade s istovjetnim ulazom.

**3. Vodeći agent dobio je strukturirani izlaz.** Odgovori u razgovoru slobodna su proza i nemjerljivi
kroz prolaze. Fiksni upit sada vraća tablicu s fiksnim stupcima; razgovorno ponašanje nije promijenjeno.

**4. Ispravljen je korpus za provjeru utemeljenosti.** Financijski izvještaji ne dolaze iz dokumenta
izvješća nego iz zasebnih SEC-ovih datoteka, pa su njihovi doslovni citati prije bili ocjenjivani kao
neutemeljeni.

**5. Greške radnika broje se po prolazu.** Neuspio poziv radnika vraća prazan rezultat, nerazlučiv od
isječka koji doista nije sadržavao ništa. Bez brojanja bi tiho smanjivao prinos i zadržavanje.

