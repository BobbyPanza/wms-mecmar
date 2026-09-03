# Script SQL — WMS su FactoryMecmar

Gli script vanno applicati al database **FactoryMecmar** (ERP Intesi).
Usano `CREATE OR ALTER` — sono idempotenti, si possono rieseguire.

| File | Contenuto |
|------|-----------|
| `V001__WMS_V_PickList.sql` | Vista `dbo.WMS_V_PickList` — lista prelievo per bolla |
| `V002__WMS_Pick_SPs.sql` | SP prelievo produzione (`WMS_OpenPickSession`, `WMS_InsertPickLine`, `WMS_ClosePickSession`, `WMS_InsertPick`) |
| `V003__WMS_V_AcceptanceDocs.sql` | Vista documenti di accettazione |
| `V004__WMS_V_ArticleDocuments.sql` | Vista documenti articolo |
| `V005__WMS_V_ArticleOrders_Engaged.sql` | Vista ordini/impegnato articolo |
| `V006__WMS_Versamento_SP.sql` | SP versamento (`WMS_InsertVersamentoLine`) |
| `V007__WMS_V_PickListBolleDetail.sql` | TVF `WMS_FN_PickListBolleDetail` — **su Logic DB**, per report Crystal |
| `V008__WMS_V_PickList_PrelevatoPerLotto.sql` | Fix `WMS_V_PickList`: `QtaPrelevata` aggregata per lotto (CONUM/LOCOD) invece che per singola bolla — **sostituisce V001** |
| `V009__XV_SITUAZIONE_PRELIEVI_Giacenza.sql` | Fix vista del report web Intesi: giacenza da locazione principale (`LCPRC='Y'`) invece di `01`/`01` cablato |
| `V010__L_MLPA_VerifiedBy.sql` | Colonne custom `L_MLPA.X_VerifiedUser` / `X_VerifiedDate` — traccia chi ha verificato l'abbinamento articolo/locazione in rettifica |

## Ordine di applicazione

```
sqlcmd -S <server> -d FactoryMecmar -U <user> -P <pwd> -i V001__WMS_V_PickList.sql
sqlcmd -S <server> -d FactoryMecmar -U <user> -P <pwd> -i V002__WMS_Pick_SPs.sql
...
sqlcmd -S <server> -d FactoryMecmar -U <user> -P <pwd> -i V008__WMS_V_PickList_PrelevatoPerLotto.sql
sqlcmd -S <server> -d FactoryMecmar -U <user> -P <pwd> -i V009__XV_SITUAZIONE_PRELIEVI_Giacenza.sql
```

`V007` va applicato al **Logic DB**, non a FactoryMecmar. Tutti gli altri a FactoryMecmar.

## Dipendenze ERP richieste

- `dbo.GetProgressivo` — assegna progressivi univoci (standard Intesi, già presente)
- `dbo.TRD_InsertMov` — scrive movimenti di magazzino (standard Intesi, già presente)
- Tabelle: `L_ODLA`, `A_LOT`, `L_CMFE`, `a_THP`, `A_PAR`, `L_MLPA`, `S_PAP`, `S_SPP`, `S_SES`, `S_MOV`
