# EdgeLink

Własne przejścia kursora między monitorami w Windows 11 — niezależne od tego, jak monitory są
ułożone w ustawieniach systemu.

Połączenie łączy **fragment krawędzi** jednego monitora z **fragmentem krawędzi** drugiego. Fragmenty
mogą mieć różną długość, a pozycja kursora jest przeliczana proporcjonalnie, więc mały odcinek może
prowadzić na całą krawędź innego ekranu. Na jednej krawędzi może być dowolnie wiele połączeń.

## Uruchomienie

Wymaga [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet publish src/EdgeLink -c Release -o publish
publish\EdgeLink.exe
```

Aplikacja siedzi w zasobniku systemowym. Konfiguracja: `%APPDATA%\EdgeLink\config.json`.

## Obsługa

- **Nowe połączenie:** złap krawędź monitora na schemacie i przeciągnij do krawędzi innego monitora.
- **Zakres:** przeciągaj uchwyty na końcach albo cały odcinek; dokładne wartości w pikselach wpisuje się w panelu po prawej.
- **Usuwanie:** `Delete` albo prawy przycisk na odcinku.
- **Pauza:** `Ctrl+Alt+Shift+E` lub menu ikony w zasobniku. Zmiany działają od razu, więc ten skrót
  jest też wyjściem awaryjnym, gdyby kursor utknął.
- **Blokuj krawędzie poza połączeniami:** na monitorze, który ma choć jedno działające połączenie,
  kursor wychodzi tylko przez połączenia. Monitor bez połączeń zachowuje się jak w zwykłym Windows.

## Rozpoznawanie monitorów

Połączenia są przypięte do profilu monitora w EdgeLink, a nie do numeru czy nazwy z Windows.
Przy każdej zmianie zestawu ekranów profile są dopasowywane na nowo:

- zwykłe monitory — po numerze seryjnym i modelu z EDID oraz porcie,
- wyświetlacze wirtualne bez EDID (spacedesk i podobne) — po rozdzielczości, karcie i położeniu,
  bo ich ścieżka urządzenia zależy tylko od kolejności podłączenia.

Odłączony monitor nie traci połączeń — czekają na jego powrót. Jeśli dwa nieodróżnialne ekrany
zostaną pomylone, kliknij monitor i użyj „Zamień z…”. „Pokaż, który to monitor” wyświetla nazwy na ekranach.

## Ograniczenia

- Bez uprawnień administratora Windows nie przekazuje ruchów myszy, gdy aktywne jest okno
  uruchomione jako administrator. Rozwiązanie: autostart „Jako administrator”.
- Sterowane są tylko ruchy fizycznej myszy. Ruch wstrzykiwany programowo (zdalny pulpit, dotyk na
  tablecie, automatyzacja) jest przepuszczany bez zmian.
- Gdy inny program zamyka kursor w swoim oknie (gry), EdgeLink się nie wtrąca.

## Budowa

- `src/EdgeLink.Core` — model, dopasowywanie monitorów i logika przejść; bez zależności od Windows.
- `src/EdgeLink` — aplikacja WPF, hak myszy, odczyt monitorów.
- `tests/EdgeLink.Tests` — `dotnet test`.
