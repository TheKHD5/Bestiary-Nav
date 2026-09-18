'use client';
import { useCallback, useEffect, useState } from 'react';
import { Activity, ArrowUpRight, Download, LockKeyhole, RefreshCw, Users, ShieldCheck } from 'lucide-react';
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Skeleton } from '@/components/ui/skeleton';
import { z } from 'zod';
const overviewSchema = z.object({
  updatedAt: z.string(), activeNow: z.number().nonnegative(), dailyActive: z.number().nonnegative(), monthlyActive: z.number().nonnegative(), reportingInstallations: z.number().nonnegative(),
  days: z.array(z.object({ day: z.string(), active: z.number().nonnegative() })), versions: z.array(z.object({ version: z.string(), installations: z.number().nonnegative() })),
  downloads: z.object({ total: z.number().nonnegative(), releases: z.array(z.object({ version: z.string(), downloads: z.number().nonnegative(), url: z.string().url() })), updatedAt: z.string() }).nullable(),
});
type Overview = {
  updatedAt: string; activeNow: number; dailyActive: number; monthlyActive: number; reportingInstallations: number;
  days: { day: string; active: number }[]; versions: { version: string; installations: number }[];
  downloads: { total: number; releases: { version: string; downloads: number; url: string }[]; updatedAt: string } | null;
};
const number = (v: number | undefined) => v === undefined ? '—' : v.toLocaleString();
export default function Dashboard() {
  const [data, setData] = useState<Overview | null>(null);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState('');
  const refresh = useCallback(async (signal?: AbortSignal) => {
    setBusy(true);
    try {
      const r = await fetch('/api/overview', { cache: 'no-store', signal });
      const result = await r.json();
      if (!r.ok) throw new Error(result && typeof result === 'object' && 'error' in result && typeof result.error === 'string' ? result.error : 'Could not load usage.');
      const parsed = overviewSchema.safeParse(result);
      if (!parsed.success) throw new Error('The reporting response could not be read. Try refreshing shortly.');
      setData(parsed.data); setError('');
    } catch (e) { if (!signal?.aborted) setError(e instanceof Error ? e.message : 'Could not load usage.'); }
    finally { if (!signal?.aborted) setBusy(false); }
  }, []);
  useEffect(() => {
    const controller = new AbortController(); void refresh(controller.signal);
    const interval = setInterval(() => { if (document.visibilityState === 'visible') void refresh(controller.signal); }, 60000);
    return () => { controller.abort(); clearInterval(interval); };
  }, [refresh]);
  const metrics = [
    { title: 'Active now', value: data?.activeNow, hint: 'Checked in within 10 minutes', icon: Activity },
    { title: 'Daily active', value: data?.dailyActive, hint: 'Participating installs · past 24 hours', icon: Users },
    { title: 'Monthly active', value: data?.monthlyActive, hint: 'Participating installs · past 30 days', icon: Users },
    { title: 'Reporting installations', value: data?.reportingInstallations, hint: 'Distinct IDs retained · past 90 days', icon: ShieldCheck },
  ];
  return <div className="dashboard-shell">
    <header className="topbar"><a href="/" className="brand"><img src="/icon.png" width="38" height="38" alt="" /><span>Bestiary Nav <span className="brand-divider">/</span> <span className="muted">Insights</span></span></a><span className="private-badge"><LockKeyhole size={14} />Owner only</span></header>
    <main className="dashboard-main">
      <div className="page-heading"><div><p className="eyebrow">YOUR COMMUNITY, AT A GLANCE</p><h1>Plugin usage</h1><p className="muted">A private view of opt-in reporting and release downloads.</p></div><Button variant="outline" onClick={() => void refresh()} disabled={busy}><RefreshCw className={busy ? 'spin' : ''} />Refresh</Button></div>
      {error && <div className="error-banner" role="alert">{error}{data && ' Showing the last successful snapshot.'}</div>}
      <div className="metrics-grid">{metrics.map((m, i) => <section key={m.title} className={`metric ${i === 0 ? 'metric-primary' : ''}`}><div className="metric-title">{m.title}<m.icon size={18} /></div>{busy && !data ? <Skeleton className="h-12 w-24 my-3" /> : <p className="metric-value">{number(m.value)}</p>}<p className="metric-hint">{m.hint}</p></section>)}</div>
      <div className="analysis-grid"><section className="panel"><div className="section-heading"><div><h2>Daily activity</h2><p className="muted">Unique reporting installations per UTC day</p></div><span className="period-label">30 days</span></div>
        <div className="chart-wrap" role="img" aria-label="Daily reporting installations for the past 30 days">{data ? data.days.some(d => d.active > 0) ? <ResponsiveContainer width="100%" height="100%"><AreaChart data={data.days} margin={{ top: 18, right: 12, left: -22, bottom: 0 }}><defs><linearGradient id="activity-fill" x1="0" x2="0" y1="0" y2="1"><stop offset="0%" stopColor="#57d9a3" stopOpacity={0.32} /><stop offset="100%" stopColor="#57d9a3" stopOpacity={0.01} /></linearGradient></defs><CartesianGrid vertical={false} stroke="#29352f" /><XAxis dataKey="day" tickFormatter={d => d.slice(5)} minTickGap={34} tick={{ fill: '#9dadab', fontSize: 12 }} axisLine={false} tickLine={false} /><YAxis allowDecimals={false} tick={{ fill: '#9dadab', fontSize: 12 }} axisLine={false} tickLine={false} /><Tooltip contentStyle={{ background: '#18231f', border: '1px solid #35483e', borderRadius: 10, color: '#f0f6f2' }} /><Area dataKey="active" name="Reporting installations" type="monotone" stroke="#57d9a3" strokeWidth={2.5} fill="url(#activity-fill)" /></AreaChart></ResponsiveContainer> : <div className="chart-empty"><Activity size={30} /><strong>Waiting for the first check-in</strong><p>Activity appears when users enable usage reporting in Bestiary Nav.</p></div> : <Skeleton className="h-full w-full" />}</div><p className="panel-footnote">One installation counts once per day, however many check-ins it sends.</p></section>
        <section className="panel"><div className="section-heading"><div><h2>Versions in use</h2><p className="muted">Latest reported version · past 30 days</p></div></div><div className="versions">{data?.versions.length ? data.versions.map(v => <div className="version-row" key={v.version}><div><strong>{v.version}</strong><span>{number(v.installations)}</span></div><div className="version-track"><span style={{ width: `${Math.max(2, v.installations / Math.max(1, data.monthlyActive) * 100)}%` }} /></div></div>) : <p className="muted empty-small">{data ? 'No versions reported yet.' : 'Loading versions…'}</p>}</div><p className="panel-footnote">Only installations that chose to report are included.</p></section></div>
      <section className="panel downloads-panel"><div className="section-heading"><div><h2><Download size={19} />Release downloads</h2><p className="muted">GitHub ZIP downloads · includes updates and repeat downloads</p></div><div className="download-total"><strong>{number(data?.downloads?.total)}</strong><span>across retained releases</span></div></div>{data?.downloads ? <Table><TableHeader><TableRow><TableHead>Release</TableHead><TableHead className="text-right">ZIP downloads</TableHead><TableHead className="text-right">Details</TableHead></TableRow></TableHeader><TableBody>{data.downloads.releases.slice(0, 8).map(r => <TableRow key={r.version}><TableCell className="font-medium">{r.version}</TableCell><TableCell className="text-right tabular-nums">{number(r.downloads)}</TableCell><TableCell className="text-right"><a className="release-link" href={r.url} target="_blank" rel="noreferrer">GitHub <ArrowUpRight size={14} /></a></TableCell></TableRow>)}</TableBody></Table> : <p className="muted empty-small">{data ? 'GitHub download counts are temporarily unavailable.' : 'Loading releases…'}</p>}</section>
      <footer className="dashboard-footer"><div><LockKeyhole size={15} /><span>Opt-in estimates, not a count of people. Reset IDs and reinstalls can count again. Records expire after 90 days.</span></div><span>{data ? `Updated ${new Date(data.updatedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}` : 'Waiting for data'} · refreshes every minute</span></footer>
    </main></div>;
}
