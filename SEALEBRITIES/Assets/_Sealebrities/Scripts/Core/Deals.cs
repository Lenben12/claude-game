using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// K.7 sponsor deals – state + rules (core). The livestream module's SponsorService creates offers from
    /// Data/SponsorDatabase.cs (AddOffer, host), checks the conditions (progress fields) and ends deals (Complete / Fail);
    /// the UI's Deals app („Barnacle“) calls Accept / Cancel. Rules:
    /// * max 3 open offers (each expires after 2 in-game days), max 2 ACTIVE deals;
    /// * two active deals may not share a topic tag – an overlapping offer cannot be accepted (even if it pays more);
    /// * cancelling pays nothing and that sponsor sulks for 3 days (no offers).
    /// Co-op: one shared list on the host; any player may accept/cancel (CoreCommands.DealKey "accept"/"cancel").
    public static class Deals
    {
        public const int MaxActive = 2, MaxOffers = 3, OfferDays = 2, SulkDays = 3;

        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;
        static readonly List<DealState> empty = new List<DealState>();

        /// Set by the livestream module: sponsorId → localized sponsor name, dealId → localized deal title.
        public static Func<string, string> SponsorName;
        public static Func<string, string> DealTitle;

        public static event Action Changed;
        public static void RaiseChanged() => Changed?.Invoke();

        public static IReadOnlyList<DealState> Offers => S != null ? (IReadOnlyList<DealState>)S.sponsor.offers : empty;
        public static IReadOnlyList<DealState> Active => S != null ? (IReadOnlyList<DealState>)S.sponsor.active : empty;

        public static string NameOf(string sponsorId) => SponsorName != null ? SponsorName(sponsorId) ?? sponsorId : sponsorId;
        public static string TitleOf(DealState d) => d == null ? "" : DealTitle != null ? DealTitle(d.dealId) ?? d.dealId : d.dealId;

        /// Localized topic tag ("shark" → „Hai“).
        public static string TagName(string tag)
        {
            switch (tag)
            {
                case "shark": return Loc.Tr("Hai", "Shark");
                case "turtle": return Loc.Tr("Schildkröte", "Turtle");
                case "ray": return Loc.Tr("Rochen", "Ray");
                case "octopus": return Loc.Tr("Krake", "Octopus");
                case "school": return Loc.Tr("Schwarm", "School");
                case "wreck": return Loc.Tr("Wrack", "Wreck");
                case "night": return Loc.Tr("Nacht", "Night");
                case "depth": return Loc.Tr("Tiefe", "Depth");
                case "fishing": return Loc.Tr("Angeln", "Fishing");
                case "livestream": return "Livestream";
                case "island": return Loc.Tr("Insel", "Island");
                case "coral": return Loc.Tr("Korallen", "Coral");
                case "whale": return Loc.Tr("Wal", "Whale");
                default: return tag ?? "";
            }
        }

        static DealState FindIn(List<DealState> list, string id)
        {
            if (list == null || id == null) return null;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].id == id) return list[i];
            return null;
        }

        public static bool IsSulking(string sponsorId)
        {
            var s = S;
            if (s == null) return false;
            var c = s.sponsor.cooldowns;
            for (int i = 0; i < c.Count; i++) if (c[i] != null && c[i].sponsorId == sponsorId && c[i].untilDay > s.day) return true;
            return false;
        }

        /// Can this offer be accepted now? conflict/tag name the overlapping active deal (K.7 disabled button text:
        /// „Überschneidung mit „{deal}“ ({tag}). Erst kündigen.“).
        public static bool CanAccept(DealState offer, out string reason, out DealState conflict, out string tag)
        {
            reason = null; conflict = null; tag = null;
            var s = S;
            if (s == null || offer == null) { reason = "?"; return false; }
            var active = s.sponsor.active;
            for (int i = 0; i < active.Count; i++)
            {
                var a = active[i];
                if (a == null || a.tags == null || offer.tags == null) continue;
                for (int x = 0; x < offer.tags.Length; x++)
                    for (int y = 0; y < a.tags.Length; y++)
                        if (offer.tags[x] == a.tags[y])
                        {
                            conflict = a;
                            tag = offer.tags[x];
                            reason = Loc.Tr($"Überschneidung mit „{TitleOf(a)}“ ({TagName(tag)}). Erst kündigen.",
                                            $"Overlaps with \"{TitleOf(a)}\" ({TagName(tag)}). Cancel it first.");
                            return false;
                        }
            }
            if (active.Count >= MaxActive)
            {
                reason = Loc.Tr("Schon 2 Deals aktiv", "Already 2 active deals");
                return false;
            }
            return true;
        }

        /// Accepts an open offer. SHARED STATE: clients forward DealKey "accept".
        public static bool Accept(string offerId)
        {
            var s = S;
            if (s == null) return false;
            var offer = FindIn(s.sponsor.offers, offerId);
            if (offer == null) return false;
            if (!CanAccept(offer, out string reason, out _, out string tag))
            {
                // L.10: icon + one short line + chip (the full sentence stays on the offer card in the app)
                if (tag != null) GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Thema schon belegt", "Topic already taken"), TagName(tag));
                else GameEvents.RaiseNotify(NotifyKind.Warning, reason);
                return false;
            }
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.DealKey, "accept", offerId);
            s.sponsor.offers.Remove(offer);
            offer.acceptedDay = s.day;
            offer.deadlineDay = s.day + Mathf.Max(1, offer.days);
            offer.progress = 0f;
            s.sponsor.active.Add(offer);
            GameEvents.RaiseDealChanged(offer.id);
            Changed?.Invoke();
            return true;
        }

        /// Cancels an active deal: no money, the sponsor sulks for 3 days. SHARED STATE (DealKey "cancel").
        public static bool Cancel(string dealId)
        {
            var s = S;
            if (s == null) return false;
            var d = FindIn(s.sponsor.active, dealId);
            if (d == null) return false;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.DealKey, "cancel", dealId);
            s.sponsor.active.Remove(d);
            Sulk(s, d.sponsorId);
            GameEvents.RaiseDealChanged(d.id);
            Changed?.Invoke();
            return true;
        }

        static void Sulk(GameState s, string sponsorId)
        {
            var c = s.sponsor.cooldowns;
            for (int i = 0; i < c.Count; i++)
                if (c[i] != null && c[i].sponsorId == sponsorId) { c[i].untilDay = s.day + SulkDays; return; }
            c.Add(new SponsorCooldown { sponsorId = sponsorId, untilDay = s.day + SulkDays });
        }

        /// Host: adds an offer (SponsorService). Refused when 3 offers are open or the sponsor sulks. The id is made
        /// unique ("{dealId}#{n}"). Notification „Neues Angebot · {Sponsor}“.
        public static bool AddOffer(DealState offer)
        {
            var s = S;
            if (s == null || offer == null || !Net.HasAuthority) return false;
            if (s.sponsor.offers.Count >= MaxOffers || IsSulking(offer.sponsorId)) return false;
            offer.id = (offer.dealId ?? "deal") + "#" + (++s.sponsor.nextId).ToString(CultureInfo.InvariantCulture);
            offer.offeredDay = s.day;
            if (offer.expiresDay <= s.day) offer.expiresDay = s.day + OfferDays;
            s.sponsor.offers.Add(offer);
            GameEvents.RaiseNotify(NotifyKind.Sponsor, Loc.Tr("Neues Angebot", "New offer"), NameOf(offer.sponsorId));
            GameEvents.RaiseDealChanged(offer.id);
            Changed?.Invoke();
            return true;
        }

        /// Host: a deal's condition is met → reward, „Deal erfüllt · +n $“.
        public static bool Complete(string dealId)
        {
            var s = S;
            var gm = GameManager.I;
            if (s == null || gm == null || !Net.HasAuthority) return false;
            var d = FindIn(s.sponsor.active, dealId);
            if (d == null) return false;
            s.sponsor.active.Remove(d);
            if (!string.IsNullOrEmpty(d.dealId)) s.sponsor.done.Add(d.dealId);
            if (d.reward > 0) gm.AddMoney(d.reward);
            GameEvents.RaiseNotify(NotifyKind.Sponsor, Loc.Tr("Deal erfüllt", "Deal done"), "+" + Loc.Money(d.reward));
            GameEvents.RaiseDealChanged(d.id);
            Changed?.Invoke();
            return true;
        }

        /// Host: deadline missed or a „no …“ rule broken → no money, angry mail „Enttäuscht. – {Sponsor}“.
        public static bool Fail(string dealId)
        {
            var s = S;
            if (s == null || !Net.HasAuthority) return false;
            var d = FindIn(s.sponsor.active, dealId);
            if (d == null) return false;
            s.sponsor.active.Remove(d);
            s.sponsor.failed++;
            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Enttäuscht.", "Disappointed."), "– " + NameOf(d.sponsorId));
            GameEvents.RaiseDealChanged(d.id);
            Changed?.Invoke();
            return true;
        }

        /// Host: progress of an active deal (SponsorService) – synced with the snapshot.
        public static void SetProgress(string dealId, float progress, float target)
        {
            var s = S;
            if (s == null || !Net.HasAuthority) return;
            var d = FindIn(s.sponsor.active, dealId);
            if (d == null) return;
            if (Mathf.Approximately(d.progress, progress) && Mathf.Approximately(d.target, target)) return;
            d.progress = progress;
            d.target = target;
            Changed?.Invoke();
        }

        /// Host, StartNewDay: offers expire, overdue deals fail, sulking ends.
        public static void OnNewDay(GameState s)
        {
            if (s == null || !Net.HasAuthority) return;
            var offers = s.sponsor.offers;
            for (int i = offers.Count - 1; i >= 0; i--) if (offers[i] == null || offers[i].expiresDay <= s.day) offers.RemoveAt(i);
            var active = s.sponsor.active;
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i] != null && active[i].deadlineDay > 0 && active[i].deadlineDay <= s.day) Fail(active[i].id);
            var c = s.sponsor.cooldowns;
            for (int i = c.Count - 1; i >= 0; i--) if (c[i] == null || c[i].untilDay <= s.day) c.RemoveAt(i);
            Changed?.Invoke();
        }

        /// Host side of DealKey.
        public static bool Execute(string op, string id)
        {
            switch (op)
            {
                case "accept": return Accept(id);
                case "cancel": return Cancel(id);
            }
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SponsorName = null; DealTitle = null; Changed = null; }
    }
}
