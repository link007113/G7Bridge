# G7 Bridge — Nederlandse handleiding

Gebruik je **GameSir G7 Pro via de 2,4GHz-ontvanger** als virtuele Steam Controller, inclusief vier onafhankelijke extra knoppen, Share en gyro. G7 Bridge verbindt de controller automatisch; Steam Input verzorgt de acties per spel.

**[Download de Windows-installer](https://github.com/link007113/G7Bridge/releases)** · [English](../README.md)

## Installeren

1. Download **`G7Bridge-1.3.0-rc.1-Setup-x64.exe`** bij Releases. De broncodezip hoef je niet te downloaden.
2. Sluit een eventueel geopende G7 Bridge en voer de installer uit. Bevestig de Windows-beheerdersvraag en lees en accepteer de voorwaarden van het meegeleverde Microsoft GameInput-onderdeel. De app, .NET-runtime, dienst en benodigde drivers zitten in dit bestand. USB-apparaten kunnen bij de eerste driverinstallatie kort opnieuw verbinden. Herstart Windows als de installer daarom vraagt.
3. Sluit GameSir Nexus, steek de 2,4GHz-ontvanger in en zet de controller aan.
4. Open **G7 Bridge** via Start of de bureaubladsnelkoppeling. Zodra de verbinding gereed is, gaat het venster naar het systeemvak.
5. Kijk in **Steam → Instellingen → Controller** naar **Steam Controller**. Zet Steam Input aan voor je spel en wijs de extra knoppen en gyro daar toe.

Vereist: **Windows 11 24H2 of nieuwer, x64**, een G7 Pro met 2,4GHz-dongle en Steam met ondersteuning voor de Steam Controller 2026. Windows 10, ARM64, Linux, Bluetooth, bedraad gebruik en andere GameSir-modellen vallen buiten deze release.

De app en installer zijn **Engels**, behalve wanneer de **Windows-weergavetaal Nederlands** is. Zowel Nederlands (Nederland) als Nederlands (België) wordt herkend. Je land, toetsenbord en datuminstellingen bepalen deze keuze niet. Na een taalwijziging moet je de app opnieuw openen.

De eigen installer heeft nog geen codeondertekening. Windows kan daarom een melding over een onbekende uitgever of SmartScreen tonen. Schakel Windows-beveiliging niet uit. Download via deze repository; `SHA256SUMS.txt` staat bij de release. De meegeleverde driverinstallers behouden hun oorspronkelijke handtekeningen.

## Functies

- Gewone knoppen, sticks en triggers worden doorgegeven. Sticks behouden de Windows/GIP-resolutie; triggers gebruiken de fysieke analoge GameSir-stand met 256 stappen.
- **L4, R4, L5 en R5** zijn vier onafhankelijke gripknoppen in Steam Input.
- **Share** wordt een aparte Quick Access-invoer. Koppel daar in Steam Input de screenshotactie aan.
- **Gyro, accelerometer, relatieve oriëntatie en accupercentage** worden doorgegeven. De accu staat ook in het systeemvak, met een melding bij 20% en 10%.
- **Gewone rumble op de twee hoofdmotoren** werkt via de Windows-gamepad-API, inclusief stoppen. Triggermotoren en speciale Steam-trackpadhaptiek worden nog niet aangestuurd.
- De G7 heeft geen trackpads. De virtuele touchoppervlakken blijven onaangeraakt. Hardwareknoppen voor koppelen, profielen en instellingen blijven controllerfuncties.

Een XInput-spel krijgt de acties die je in Steam Input instelt. De bridge voegt geen nieuwe gyro-API aan het spel zelf toe. Bestaande firmware-remapping hoort geen dubbele A/B-actie van een achterknop te veroorzaken.

De analoge triggerstand wordt vóór de verwerking van het controllerprofiel gelezen. Een daarin ingestelde curve of hair-triggerbewerking wordt dus niet toegepast op de virtuele triggerwaarden. Spelspecifiek gedrag kun je in Steam Input instellen; de bridge herschrijft je controllerprofiel niet. Verouderde fysieke telemetrie laat de triggers los totdat verse invoer terugkomt.

## Dagelijks gebruik

Het venster toont een **live controllertekening**. Knoppen lichten op, stickmarkeringen bewegen en triggers tonen hun percentage. Ook L4/R4/L5/R5, Guide en Share staan erop. Drie gyrobalkjes tonen de draaisnelheid in graden per seconde. De tekst bij de laatste knop bewaart ook een korte druk.

De weergave leest de eigen **virtuele Steam Controller via Windows terug**. Zo zie je welke invoer Windows ontvangt. De spelacties hangen vervolgens af van je Steam Input-indeling. Bij verouderde of weggevallen invoer wordt de weergave neutraal. Het uitlezen stopt als het venster verborgen is.

Met **Triltest (0,4 s)** laat je beide hoofdmotoren kort en op gematigde sterkte trillen. De aanvraag loopt via de virtuele controller terug door de bridge. De knop werkt alleen bij verse virtuele invoer en een actieve bridge. Minimaliseren of afsluiten annuleert de test; aan het einde wordt een stopbericht gestuurd. De test start nooit vanzelf en stuurt de triggermotoren niet aan.

**Minimaliseren** laat de bridge actief. Dubbelklik op het systeemvakicoon of open de snelkoppeling opnieuw om het venster terug te halen. **Uitzetten** stopt de bridge; **Afsluiten** sluit ook de app. Daarbij wordt de fysieke controller weer vrijgegeven.

**Starten met Windows** (in het venster en het systeemvakmenu, standaard aan) start G7 Bridge bij het aanmelden in het systeemvak. De bridge wacht dan op de controller en verbindt zodra je hem aanzet. Dit is een instelling per gebruiker zonder beheerdersvraag. Je kunt hem ook uitzetten via **Instellingen → Apps → Opstarten**.

Zolang de controller uit staat, opent de bridge hem niet. Hij kijkt twee keer per seconde in de apparatenlijst van Windows en laat de eigen verbergregel staan, zodat de controller al verborgen is voor andere programma's zodra hij verbindt. Lukt verbinden drie keer niet terwijl de controller aan staat (ook als Windows hem binnen tien seconden niet meldt), dan geeft de bridge de fysieke controller vrij voor normaal gebruik totdat je de controller uit en weer aan zet. Direct na het insteken van de ontvanger, vóór de eerste gyrostart, geldt de verbergregel nog niet; de bridge blijft het dan gewoon proberen.

Het **systeemvakicoon** is een accu. Groen betekent actief met de accustand (rood als hij bijna leeg is), grijs betekent wachten of uit, en oranje betekent dat er iets aandacht nodig heeft; een bliksem betekent opladen. Beweeg eroverheen voor het precieze percentage. Bij 20% en 10% krijg je één keer per ontlaadbeurt een Windows-melding. Een aandachtspunt wordt één keer gemeld als het venster verborgen is.

Nexus en Windows-ontwikkelaarsmodus zijn tijdens gebruik niet nodig. De dienst wordt op verzoek van de app gestart, niet automatisch bij het opstarten van Windows. Dagelijks gebruik vraagt geen beheerdersrechten.

## Bijwerken en verwijderen

Sluit G7 Bridge en voer een nieuwe installer uit om bij te werken. Instellingen blijven behouden.

Verwijderen gaat via **Windows-instellingen → Apps → Geïnstalleerde apps → G7 Bridge → Verwijderen**. Eerst afsluiten via het systeemvak. De uninstaller ruimt de eigen dienst, verbergregels, appbestanden, snelkoppelingen en de opstartinstelling van de verwijderende gebruiker op. De gedeelde HidHide- en virtuele USB-drivers blijven staan omdat andere programma's ze kunnen gebruiken. Instellingen en diagnosebestanden blijven in `%PROGRAMDATA%\G7Bridge`.

De app staat in `C:\Program Files\Grimm\G7Bridge`. De dienst `GrimmG7Bridge` draait als LocalSystem om GIP-achtergrondinvoer te lezen. Alleen de legacy-invoerinstanties van de geselecteerde GameSir worden voor games verborgen. Andere apparaten en bestaande HidHide-regels blijven behouden. Er worden geen fysieke firmware, controllerprofielen of Steam-updaterbestanden herschreven.

## Als iets niet werkt

- **Wachten op de G7 Pro:** zet hem aan, haal hem uit de dock, laat de dongle aangesloten en sluit Nexus.
- **Meerdere ontvangers:** sluit alleen de ontvanger aan die je wilt gebruiken.
- **Drivers nog niet beschikbaar:** herstart Windows na installatie; voer zo nodig de installer opnieuw uit.
- **Dubbele controllerinvoer:** sluit bridge en spel, open eerst de bridge en start het spel pas zodra de bridge gereed is.
- **Firmwaremelding van Steam:** sluit de bridge, werk Steam normaal bij en open de bridge opnieuw. Start geen firmwareupdate voor de virtuele controller; meld het probleem als het terugkomt.
- **Gyro of achterknoppen doen niets:** controleer Steam Input en de knop-/gyroacties in de spelindeling.
- **Installatiefout:** details staan in `%PROGRAMDATA%\G7Bridge\setup-error.txt` en het Inno Setup-log in `%TEMP%`.

Kies **Diagnose opslaan** bij een foutmelding. De export bevat onder meer apparaat-ID's, USB-locatie, invoertoestand en recente servicemeldingen. Kijk deze na voordat je ze openbaar deelt. Technische servicelogs gebruiken Engels.

## Status van deze release

**1.3.0-rc.1 is een preview.** De bestaande bridge is met een echte G7 Pro gemeten. De nieuwe installer, upgrade- en verwijderprocedure moeten nog handmatig op een schone Windows-installatie worden beoordeeld. Er zijn offline unittests en builds uitgevoerd; dat vervangt die installatieproef niet. Zie [de precieze validatiestatus](VALIDATION.md).

[Broncode bouwen](BUILD.md) · [Onderdelen en licenties](../DEPENDENCIES.md)

De oorspronkelijke Microsoft-voorwaarden blijven in het Engels beschikbaar. Bij gebruik of herdistributie van de meegeleverde onderdelen gelden hun eigen voorwaarden; zie [NOTICE.md](../NOTICE.md).
