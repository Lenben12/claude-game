using UnityEngine;

namespace Sealebrities
{
    /// Everything the camera found in one photo (K.2 Sealog photo, K.3 challenge checks, K.4 selfies, K.7 sponsor
    /// conditions, B.4 research photo quests). Raised by FilmHandCamera.PhotoAnalyzed for every photo of the LOCAL
    /// player. ONE instance is reused for every photo: copy what you need inside the handler.
    public sealed class FilmPhotoReport
    {
        public const int MaxSpecies = 16;

        /// Gallery entry (null when saving the PNG failed – the analysis still ran).
        public PhotoEntry photo;
        /// Sealog subject (null = no valid subject: nothing big / central / bright enough).
        public SpeciesDef subject;
        public ICreature subjectCreature;
        public float subjectCm;
        public SizeTier subjectTier;
        public float subjectDistance;
        /// Meters below the surface (0 on land).
        public float subjectDepth;
        /// Fraction of the frame the subject fills (max of height / width).
        public float subjectFill;
        public Vector3 subjectPos;
        /// Photo quality 0..1 of the subject (0 without a subject).
        public float quality;
        /// The Sealog result of this photo.
        public bool isNew, isRecord;
        /// Selfie view photo (K.4) with the expression shown and whether it fitted the scene.
        public bool selfie;
        public int selfieExpression;
        public bool selfieMatch;
        /// Animals detected in the photo and their distinct species (first speciesCount entries, subject first).
        public int animalCount;
        public int speciesCount;
        public readonly SpeciesDef[] species = new SpeciesDef[MaxSpecies];
        /// Lens state.
        public Vector3 cameraPos;
        /// Degrees, negative = looking down (ch_ray_top: < −45°).
        public float cameraPitch;
        public float cameraDepth;
        public bool lampOn, nightVision;
        /// Time of day 0..1 and the L.1 zone of the lens.
        public float timeOfDay;
        public string zone;
        /// Animal event running near the subject / lens (K.3), null = none.
        public string eventId;
        /// Post-finale legend photographed (FilmLegends id) / gold record (≥ maxCm × 1.05).
        public string legendId;
        public bool goldRecord;

        public bool HasSpecies(string id)
        {
            for (int i = 0; i < speciesCount; i++) if (species[i] != null && species[i].id == id) return true;
            return false;
        }

        public bool HasTag(string tag)
        {
            for (int i = 0; i < speciesCount; i++) if (FilmTags.Matches(species[i], tag)) return true;
            return false;
        }

        internal void Clear()
        {
            photo = null; subject = null; subjectCreature = null; subjectCm = 0f; subjectTier = SizeTier.Normal;
            subjectDistance = 0f; subjectDepth = 0f; subjectFill = 0f; subjectPos = Vector3.zero; quality = 0f;
            isNew = false; isRecord = false; selfie = false; selfieExpression = 0; selfieMatch = false;
            animalCount = 0;
            for (int i = 0; i < speciesCount; i++) species[i] = null;
            speciesCount = 0;
            cameraPos = Vector3.zero; cameraPitch = 0f; cameraDepth = 0f; lampOn = false; nightVision = false;
            timeOfDay = 0f; zone = null; eventId = null; legendId = null; goldRecord = false;
        }
    }
}
