# TheGang-Online

## Connexion entre réseaux différents (via relais)

Le mode "Relais" permet à un hôte et des invités situés sur des réseaux différents de se connecter sans ouvrir de port sur leur box. Démarrez le serveur de relais sur une machine accessible publiquement (VPS, serveur dédié, etc.), puis renseignez son adresse, son port et un code de session partagé dans l'interface de test peer-to-peer.

### Démarrer le relais

```bash
dotnet run --project TheGang/Relay/Relay.csproj -- 9000
```

### Utilisation dans l'application

1. Cochez **Utiliser un relais**.
2. Renseignez l'**adresse du relais**, le **port** et un **code de session**.
3. Lancez l'hôte puis connectez les invités avec le même code.
