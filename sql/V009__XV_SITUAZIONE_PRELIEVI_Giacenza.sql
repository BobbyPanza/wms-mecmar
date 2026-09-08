-- ============================================================
-- V009 — XV_SITUAZIONE_PRELIEVI: giacenza dalla locazione principale
--
-- NOTA: questa vista NON è del WMS, è la vista usata dal report web
-- Intesi sulla situazione prelievi. Lo script è qui per tracciabilità
-- perché la modifica serve ad allineare i due strumenti.
--
-- PROBLEMA
-- La giacenza era letta con locazione cablata:
--     LEFT JOIN L_MLPA t9 ON t9.pacod = t8.PACOD
--                        AND t9.MGCOD = '01' AND t9.LCCOD = '01'
-- mentre WMS_V_PickList usa la locazione principale dell'articolo
-- (L_MLPA.LCPRC = 'Y'). Finché tutte le principali stanno su 01/01
-- i due valori coincidono, ma appena un articolo viene spostato di
-- locazione principale il report mostra 0 (o la giacenza sbagliata)
-- e il WMS il valore corretto.
--
-- FIX
-- Join su LCPRC = 'Y' — verificato univoco per PACOD (0 articoli con
-- più di una principale, 0 articoli su 01/01 senza principale).
--
-- IMPATTO IMMEDIATO: NESSUNO.
-- Al 2026-09-03 tutti i 52.106 articoli con LCPRC='Y' hanno la
-- principale su MGCOD='01'/LCCOD='01', quindi i numeri del report
-- non cambiano. La modifica è preventiva.
--
-- Unica modifica rispetto alla definizione precedente: il join t9.
-- ============================================================
CREATE OR ALTER VIEW [dbo].[XV_SITUAZIONE_PRELIEVI]
as
with daPrelevare as
(
select
	t1.conum,
	t1.locod,
	t2.pacod,
	SUM(t2.LOQTP) as LOQTP,
	t2.padsc,
	t2.IDTHP,
	'E' as PATYF
FROM
	A_LOT t1
	inner join L_CMFE t2
		on t1.conum = t2.conum and t1.locod = t2.LOCLP
	GROUP BY
	t1.conum,
	t1.locod,
	t2.pacod,

	t2.padsc,
	t2.IDTHP

UNION
select
	t1.conum,
	t1.locod,
	t2.pacod,
	  SUM(CASE WHEN t1.LOQTP > 0 then t2.LOQDB*t1.LOQDB/t1.LOQTP ELSE 0 END) as LOQDB,
	t2.padsc,
	t2.IDTHP,
	'I'
FROM
	A_LOT t1
	inner join A_LOT t2
		on t1.conum = t2.conum and t1.locod = t2.LOCLP and t1.locod <> t2.LOCOD
	group by
	t1.conum,
	t1.locod,
	t2.pacod,

	t2.padsc,
	t2.IDTHP
), partiPrelevate as
(	select
	t1.conum, t1.LOCOD, t2.pacod, SUM(t2.PAQTP) as QtaPrel
	from l_ODLA t1
	inner join S_PAP t2
		on t1.olcod = t2.OLCOD
	group by t1.conum, t1.LOCOD, t2.pacod
), prelievoIniziato as
(	select
	t1.conum, t1.LOCOD, 1 as Iniz
	from l_ODLA t1
	inner join S_PAP t2
		on t1.olcod = t2.OLCOD
	group by t1.conum, t1.LOCOD
), BollaPrelievo as
(	select
	conum, locod, olcod, (select stdsc from a_Sta where stcod = lasto) as Stato, row_number() over (partition by conum, locod order by faseq) as Ordine
	from A_LAV t1
	--where t1.fatyp = 68
), BollaVersamento as
(	select
	conum, locod, MAX(OLCOD) Bolla
	from A_LAV t1 where LAUFC = 'Y' GROUP by CONUM, locod
	--where t1.fatyp = 68
)
select
	t1.COCOD , t1.CTDSC, t2.PACOD as RigaOrdine, t2.PADSC as RigaOrdineDesc, t2.CMDTS as Consegna, t1.conum
	,t3.pacod as ParteLotto, t3.padsc as ParteLottoDesc, t4.PACOD as PartePrelievo, t4.padsc as PartePrelievoDesc, t4.LOQTP as QtaDaPrelevare
	,isnull(t5.qtaprel, 0) as QtaPrelevata, isnull(t6.Iniz, 0) as PrelievoIniziato, t7.THDSC as Handling, t8.paf02 as UBICAZIONE, t9.QTLOC as Giacenza,
	t3.LOCOD, case when t1.costo = 25 then 1 else 0 end as Terminata, t10.OLCOD, t10.Stato, case when t3.losto = 35 then 1 else 0 end as LottoTerminato, t11.Bolla as BollaVersamento
	from a_COM t1
	inner join L_CMPA t2
		on t1.conum = t2.conum
	inner join A_LOT t3
		on t3.loclm = t2.locod and t3.conum = t2.CONUM
	left join daPrelevare t4
		on t4.CONUM = t3.CONUM and t4.LOCOD = t3.LOCOD
	left join partiPrelevate t5
		on t5.PACOD = t4.PACOD and t4.CONUM = t5.CONUM and t4.LOCOD = t5.LOCOD
	left join prelievoIniziato t6
		on t6.CONUM = t3.CONUM and t6.LOCOD = t3.LOCOD
	left join a_THP t7 on t4.IDTHP = t7.IDTHP
	inner join A_PAR t8 on t4.pacod = t8.PACOD
	-- V009: era MGCOD='01' AND LCCOD='01' cablato → ora locazione principale
	LEFT JOIN L_MLPA t9 on t9.pacod = t8.PACOD and t9.LCPRC = 'Y'
	left join BollaPrelievo  t10 on t10.conum = t3.conum and t10.locod = t3.locod and t10.ordine = 1
	left join BollaVersamento t11 on t11.CONUM = t3.CONUM and t11.LOCOD = t3.LOCOD
where 1=1
--and t2.stcon < 72 --and t1.COSTO < 25
--and isnull(t5.QtaPrel, 0) < t4.loqtp
GO
