#!/usr/bin/env bash
# Vérification sans Windows : tests du moteur + compilation du C# de l'interface
# (la compilation XAML n'existe que sous Windows ; le XAML est seulement contrôlé comme XML).
set -euo pipefail
cd "$(dirname "$0")"
dotnet test ../../tests/Desinstalleur.Core.Tests
python3 gen_stubs.py ../../src/Desinstalleur.App stubs
dotnet build Check.csproj
