# Uninstallix

Application Windows pour désinstaller ses logiciels plus facilement que l'outil intégré :
liste claire, recherche, tri, désinstallation en lot, détection des restes et protection
des composants essentiels.

## Technologie retenue : C# + WPF sur .NET 8

| Critère | Pourquoi WPF / .NET 8 |
|---|---|
| Accès à Windows | Lecture du registre, lancement des désinstalleurs, Corbeille, droits administrateur : tout est natif en C#, sans pont ni dépendance. |
| Interface moderne | WPF permet un style Windows 11 complet (coins arrondis, typographie Segoe UI Variable, icônes Fluent) défini dans `Themes/Styles.xaml`. |
| Simplicité | Un seul `.exe`, pas de runtime web embarqué (contrairement à Electron ou Tauri), démarrage rapide, Visual Studio Community suffit. |
| Pérennité | .NET 8 est une version LTS. WinUI 3 était l'autre option, mais son outillage et son déploiement sont plus lourds pour un gain visuel faible. |

## Ce que fait la version 0.6

Uninstallix vérifie aussi les mises à jour publiées sur GitHub (Réglages › Mises à jour).

L'application a un menu latéral avec cinq pages : **Logiciels**, **Nettoyage**, **Espace disque**, **Historique** et **Réglages**.

### Logiciels

- **Liste des logiciels** lue dans les trois emplacements du registre (64 bits, 32 bits, utilisateur), avec
  icône, éditeur, version, **taille** et **date d'installation**. Quand le logiciel ne déclare pas sa taille,
  elle est mesurée sur son dossier (affichée « ≈ »). Quand il ne déclare pas sa date, on prend la date de
  dernière modification de sa clé (affichée « ≈ »).
- **Recherche** instantanée (nom ou éditeur, sans tenir compte des accents) et **tri** par nom, taille, date
  ou éditeur, via la liste déroulante ou en cliquant sur les en-têtes de colonnes.
- **Désinstallation officielle** : on lance le désinstalleur fourni par le logiciel (MSI ou exécutable) et on
  attend sa fin, y compris quand il passe la main à une copie temporaire (NSIS, Inno Setup…).
- **Plusieurs logiciels à la suite** : cochez-les puis « Désinstaller la sélection ». Ils sont traités l'un
  après l'autre ; « Arrêter après celui-ci » interrompt la file proprement.
- **Restes** : après chaque désinstallation réussie, recherche dans Program Files, ProgramData, AppData
  (Roaming, Local, LocalLow, Programs), menu Démarrer, Bureau et registre (`HKCU\Software`, `HKLM\SOFTWARE`,
  `WOW6432Node`). Chaque élément est classé « Sûr » (nom identique, coché par défaut) ou « À vérifier »
  (ressemblance partielle, décoché par défaut).
- **Protection** des composants essentiels (runtimes Visual C++ et .NET, WebView2, pilotes, DirectX, Java,
  composants système) : ils sont marqués « Protégé » et ne peuvent pas être cochés sans déverrouillage explicite.

- **Désinstallation forcée** quand le désinstalleur du logiciel a disparu : Uninstallix propose alors de
  nettoyer lui-même ses restes et son entrée dans la liste.

### Nettoyage

- **Caches** : fichiers temporaires de Windows, FiveM (cache, server-cache, crashes, logs ; le dossier `game`
  n'est jamais touché), Discord, Chrome et Edge, Steam, Roblox, caches de shaders (NVIDIA, AMD, DirectX),
  rapports d'erreurs et Corbeille. La taille de chaque cache est mesurée, vous cochez ce que vous voulez vider,
  et une confirmation est toujours demandée. Si le logiciel concerné est ouvert, Uninstallix prévient.
- **RAM** : vide la mémoire de travail des processus et la liste « en attente » de Windows. L'effet est
  temporaire : Windows réutilise ensuite la mémoire libre selon ses besoins.
- **Cache DNS** : l'équivalent de `ipconfig /flushdns`.

- **Profils** (Personnalisé, Gaming, Navigateurs) : chacun retient sa propre sélection de caches.
- **Aperçu avant nettoyage** : chaque fichier ou dossier qui sera supprimé, avec sa taille, les fichiers conservés
  et les applications à fermer (bouton pour les fermer proprement). Un clic ouvre l'emplacement.

### Espace disque

Analyse d'un disque ou d'un dossier (lecture seule) : dossiers et gros fichiers triés par taille, avec leur part
du total. Double-clic pour descendre dans un dossier, bouton pour ouvrir l'emplacement dans l'Explorateur.

### Historique

Chaque nettoyage (caches ou restes de logiciels) est noté avec sa date, ses éléments et l'espace réellement
récupéré, enregistré dans `%LocalAppData%\Uninstallix\historique.json`.

### Favoris protégés

Dossiers et logiciels exclus de toutes les propositions de nettoyage (Réglages, ou clic droit sur un logiciel).

### Signaler un problème

Réglages › Outils : prépare un rapport (version, Windows, réglages, journal) avec masquage du nom d'utilisateur,
du nom du PC, des dossiers personnels et des e-mails, et un aperçu modifiable à copier ou enregistrer.

### Animations

Apparition en douceur des pages, des listes (en cascade) et des messages, compteurs de taille qui défilent,
barre de RAM fluide, boutons qui s'illuminent au survol et s'enfoncent au clic, interrupteurs qui glissent,
coche « pop » à la fin de l'installation. Désactivables dans Réglages › Apparence.

### Réglages

Enregistrés dans `%LocalAppData%\Uninstallix\reglages.json` : point de restauration avant les désinstallations,
désinstallation silencieuse quand le logiciel la propose, cochage automatique des restes « Sûr », Corbeille ou
suppression définitive, affichage des composants système, conservation des fichiers temporaires récents.
La page donne aussi accès au journal, aux sauvegardes du registre et à la restauration du système.

## Sécurité des suppressions

Rien n'est supprimé sans votre accord :

1. Les restes sont d'abord **présentés** ; vous cochez ce que vous voulez supprimer.
2. Une **fenêtre de confirmation** récapitule le nombre d'éléments, la taille et prévient si des éléments
   « À vérifier » sont cochés.
3. Les fichiers et dossiers partent à la **Corbeille** (restaurables).
4. Chaque clé de registre est **exportée en `.reg`** dans `%LocalAppData%\Uninstallix\Sauvegardes registre`
   avant d'être supprimée ; si la sauvegarde échoue, la clé est conservée.
5. Un garde-fou (`SafetyGuard`) revérifie chaque chemin juste avant la suppression et refuse : Windows,
   racines de lecteur, dossiers utilisateur, `Program Files` lui-même, `Common Files`, `WindowsApps`,
   dossiers Microsoft d'AppData et ProgramData, caches d'installation, liens symboliques, et tout dossier
   encore utilisé par un logiciel installé.
6. Toutes les actions sont écrites dans `%LocalAppData%\Uninstallix\journal.txt`.

## Installer

1. Double-cliquez sur **`Creer-Setup.bat`** (une seule fois, et à chaque nouvelle version). Il fabrique
   **`Uninstallix-Setup.exe`**, un fichier unique qui contient tout ce qu'il faut, même sur un PC sans .NET.
   Le fichier est créé dans ce dossier et copié sur le Bureau.
2. Double-cliquez sur **`Uninstallix-Setup.exe`** : l'assistant demande où installer (par défaut
   `C:\Program Files\Uninstallix`), s'il faut un raccourci sur le Bureau et dans le menu Démarrer, puis installe.
   Uninstallix apparaît ensuite dans « Applications installées » de Windows, d'où on peut le désinstaller.

Ce fichier peut être copié sur une clé USB ou envoyé à quelqu'un. Relancé sur un PC où Uninstallix est déjà
installé, il fait la mise à jour en gardant les réglages.

Comment ça marche : c'est le même exécutable que l'application. Quand son nom contient « Setup », il affiche
l'assistant d'installation ; une fois installé sous le nom `Uninstallix.exe`, il s'ouvre normalement ; lancé
avec `--desinstaller` (par Windows), il se retire du PC.

## Publier une nouvelle version

Le dépôt GitHub [alexyxcfghv2/Uninstallix](https://github.com/alexyxcfghv2/Uninstallix) fabrique l'installateur tout seul :

1. Changer `<Version>` dans `src/Desinstalleur.App/Desinstalleur.App.csproj` et écrire les nouveautés dans
   `release-notes/vX.Y.Z.md`.
2. Envoyer sur `main`.
3. GitHub Actions (`.github/workflows/release.yml`) voit que cette version n'est pas encore publiée, compile sous
   Windows, lance les tests, fabrique `Uninstallix-Setup.exe` et crée la version `vX.Y.Z`. Les applis déjà
   installées la proposent alors à leur prochain démarrage (ou via Réglages › Mises à jour), avec ses nouveautés ;
   l'utilisateur choisit de l'installer.

Le téléchargement est vérifié avec l'empreinte SHA-256 fournie par GitHub.

## Compiler et lancer

Prérequis : Windows 10 ou 11 et le [SDK .NET 8 ou plus récent](https://dotnet.microsoft.com/download/dotnet/8.0)
(ou Visual Studio 2022 avec la charge « Développement .NET Desktop »).

Le plus simple : double-cliquez sur **`Lancer.bat`**. Il compile l'application puis la lance
(Windows demande alors les droits administrateur).

En ligne de commande :

```powershell
cd desinstalleur
dotnet test tests\Desinstalleur.Core.Tests     # 70 tests du moteur
dotnet build src\Desinstalleur.App -c Release -o publish
start publish\Uninstallix.exe                # « dotnet run » échoue car l'appli exige les droits admin
```

Un SDK plus récent (.NET 9 ou 10) convient aussi : `Directory.Build.props` autorise l'exécution sur une version plus récente.

Pour produire un exécutable autonome :

```powershell
dotnet publish src\Desinstalleur.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

L'application demande les droits administrateur au démarrage : ils sont nécessaires pour lancer les
désinstalleurs système et nettoyer `Program Files` et `HKLM`. Sous Visual Studio, lancez-le en administrateur.

## Organisation du code

```
src/Desinstalleur.Core/       Logique pure, testable partout
  UninstallCommand.cs         Analyse de « UninstallString » (guillemets, MSI /I → /X…)
  NameMatcher.cs              Comparaison nom de logiciel ↔ nom de dossier ou de clé
  SafetyGuard.cs              Règles qui interdisent les suppressions dangereuses
  ProtectionRules.cs          Liste des composants essentiels
  ValueParsers.cs             Dates, tailles, formatage en français
src/Desinstalleur.App/        Application WPF
  Services/                   Registre, icônes, désinstallation, recherche et suppression des restes
  Views/MainWindow            Liste, recherche, tri, sélection
  Views/UninstallWindow       File de désinstallation puis validation des restes
  Themes/Styles.xaml          Style visuel
tests/Desinstalleur.Core.Tests
tools/verif-linux/            Vérification sans Windows (tests + compilation du C#)
```

## Plan de développement

**Étapes 1 et 2 (livrées)** : tout ce qui est décrit plus haut, dont le point de restauration, la
désinstallation silencieuse, le nettoyage des caches et de la RAM et l'installateur `Uninstallix-Setup.exe`.

**Prochaine étape — Fiabiliser sur de vrais PC**
- Tester sur une dizaine de logiciels courants (7-Zip, VLC, Firefox, Discord, Steam, Office…) et ajuster
  les règles de détection des restes.

**Étape 3 — Couvrir plus de logiciels**
- Applications du Microsoft Store (paquets MSIX / UWP).
- Logiciels installés par winget, Chocolatey ou Scoop.
- Restes supplémentaires : services, tâches planifiées, entrées de démarrage, associations de fichiers.

**Étape 4 — Confort**
- Thème sombre automatique, fond Mica de Windows 11.
- Historique des désinstallations et restauration en un clic des clés sauvegardées.
- Export de la liste des logiciels (CSV).
- Installeur ou version portable signée.

## État de la vérification

Le code a été écrit et vérifié sur une machine Linux, sans Windows :

- ✅ Les 70 tests du moteur passent (analyse des commandes, correspondance des noms, garde-fou, protection,
  dates et tailles).
- ✅ Tout le C# de l'application WPF compile sans erreur ni avertissement contre les bibliothèques Windows.
- ⚠️ Le XAML n'a pu être contrôlé que comme XML valide (noms, ressources et gestionnaires d'événements
  vérifiés par script) : sa compilation n'existe que sous Windows.
- ✅ Les versions 0.1 et 0.2 ont été lancées et testées sur Windows par l'utilisateur.
- ✅ La version 0.3 (menu latéral, Nettoyage, Réglages) a été lancée sur Windows par l'utilisateur.
- ✅ L'installateur (0.4), les animations et l'optimisation mémoire (0.5) ont été essayés sur Windows par l'utilisateur.
- ❌ Les nouveautés 0.6 (aperçu, profils, espace disque, historique, favoris, rapport) n'ont pas encore été essayées sur Windows.

## Licence

© 2026 alexy. **Tous droits réservés.** Uninstallix est un logiciel propriétaire, pas un logiciel libre :
vous pouvez télécharger l'installateur officiel et l'utiliser gratuitement, mais toute copie, modification,
redistribution ou réutilisation du code, du nom ou du logo est interdite sans autorisation écrite.
Voir [LICENSE](LICENSE).
