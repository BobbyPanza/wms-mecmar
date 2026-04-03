# Script SQL — WMS su FactoryMecmar

Gli script vanno applicati al database **FactoryMecmar** (ERP Intesi).
Usano `CREATE OR ALTER` — sono idempotenti, si possono rieseguire.

| File | Contenuto |
|------|-----------|
| `V001__WMS_V_PickList.sql` | Vista `dbo.WMS_V_PickList` — lista prelievo per bolla |
| `V002__WMS_Pick_SPs.sql` | SP prelievo produzione (`WMS_OpenPickSession`, `WMS_InsertPickLine`, `WMS_ClosePickSession`, `WMS_InsertPick`) |

## Ordine di applicazione

```
sqlcmd -S <server> -d FactoryMecmar -U <user> -P <pwd> -i V001__WMS_V_PickList.sql
sqlcmd -S <server> -d FactoryMecmar -U <user> -P <pwd> -i V002__WMS_Pick_SPs.sql
```

## Dipendenze ERP richieste

- `dbo.GetProgressivo` — assegna progressivi univoci (standard Intesi, già presente)
- `dbo.TRD_InsertMov` — scrive movimenti di magazzino (standard Intesi, già presente)
- Tabelle: `L_ODLA`, `A_LOT`, `L_CMFE`, `a_THP`, `A_PAR`, `L_MLPA`, `S_PAP`, `S_SPP`, `S_SES`, `S_MOV`
