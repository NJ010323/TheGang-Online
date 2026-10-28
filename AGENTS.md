# Validation locale du jeu

- Après chaque modification du jeu, régénérer le paquet Windows autonome dans
  `TheGang/artifacts/gametest` pour permettre à Nolan de tester immédiatement.
- Avant chaque build de `gametest`, consulter la dernière release stable active
  du dépôt de distribution configuré dans `TheGang/update-settings.json`
  (GitHub Releases : `/releases/latest`, hors brouillons et préversions).
  Utiliser son numéro de version pour le paquet et les exécutables, sans figer
  ce numéro dans ces consignes ni se baser uniquement sur les tags locaux.
  Après une nouvelle release, les builds suivants reprennent donc sa version.
  Si GitHub est inaccessible, utiliser la dernière version de release vérifiée
  dans la session et signaler cette limite ; sans référence vérifiée, demander
  la version plutôt que d'en inventer une.
  Ne pas augmenter la version ni créer de release sauf demande explicite.
- Publier avec `TheGang/publish-windows.ps1` dans un dossier temporaire vide,
  puis recopier le paquet vérifié vers `gametest`, sans supprimer les éventuels
  fichiers personnels. Vérifier les empreintes du manifeste et la version des
  exécutables. Ne pas interrompre un jeu en cours pour remplacer les fichiers.

# Archivage local des releases

- À chaque nouvelle release explicitement demandée, conserver le paquet Windows
  autonome correspondant dans `TheGang/artifacts/release_vX.Y.Z` (par exemple
  `release_v0.0.6`), avec le numéro exact de la release publiée.
- Y placer directement les mêmes fichiers de distribution que dans `gametest` :
  jeu, auxiliaire de mise à jour, tunnel, licence, configuration et manifeste.
  Pour cette release, alimenter `gametest` et l'archive depuis le même paquet
  vérifié, puis vérifier les versions et les empreintes SHA-256 des deux copies.
- Archiver le paquet exact publié : si les binaires GitHub Actions diffèrent
  d'un build local, récupérer le ZIP de la release et utiliser ses fichiers pour
  l'archive et pour `gametest` au moment de la publication.
- Garder les dossiers `release_v*` précédents pour constituer un historique.
  Ne pas écraser une archive existante avec des modifications ultérieures, même
  si `gametest` conserve encore ce numéro de version. Si le dossier existe,
  vérifier qu'il correspond au paquet publié ; ne jamais le remplacer sans
  demande explicite.
- Une simple modification des consignes ne nécessite pas de régénérer le jeu.
