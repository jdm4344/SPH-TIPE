﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Xml.Linq;


namespace SPH_TIPE
{



    class Particule
    {
        
        // Propriétés
        public double MasseVolumique { get; set; }
        public int Index { get; set; }
        public bool Mobile { get; set; }
        private Vector _Position;
        public Vector Position
        {
            get { return _Position; }
            set
            {
                // On bouge, on change de cellule
                try
                {
                    Systeme.Grille[Cellule].Remove(Index);
                }
                catch (ArgumentNullException ex)
                {
                    // Cas exceptionnel lors de l'initialisation, la particule n'est pas encore dans la grille
                }
                short x = (short)(Position.X / constants.rayonSPH);
                short y = (short)(Position.Y / constants.rayonSPH);
                Cellule = new Tuple<short, short>(x, y);
                //Console.WriteLine(Cellule);
                //Console.WriteLine(Systeme.Grille[Cellule].Count);
                Systeme.Grille[Cellule].Add(Index);
                _Position = value; // On affecte à la fin la nouvelle valeur de la postion
            }
        }
        public Vector Vitesse { get; set; }
        public Vector Acceleration { get; set; }
        public Tuple<short, short> Cellule { get; set; }

        // Constructeurs
        public Particule(Vector position, Vector vitesse, Vector acceleration, bool mobile = true)
        {

            this.Index = Systeme.NombreParticulesInstanciees++;
            this.Position = position;
            this.Vitesse = vitesse;
            this.Acceleration = acceleration;
            this.Mobile = mobile;
        }

        // Méthodes
        public List<int> TrouverVoisines() // On repère toujours les particules par leurs indices
        {

            // On trouve les particules dans les cellules adjacentes
            List<int> ParticulesProches = new List<int>();
            for (short i = -1; i <= 1; i++) //Améliorable avec une jolie requête LINQ?
            {
                for (short j = -1; j <= 1; j++)
                {
                    Tuple<short, short> tuple = new Tuple<short, short>((short)(Cellule.Item1 + i), (short)(Cellule.Item2 + j));
                    //Console.WriteLine((from index in Systeme.Grille[tuple] select index).ToList().Count);
                    ParticulesProches.AddRange(from index in Systeme.Grille[tuple]
                                                     where (Systeme.Particules[index].Position - this.Position).Length <= constants.rayonSPH
                                                     select index );
                }
            }

            return ParticulesProches;

        }

        public Vector CalculerAcceleration(List<int> Voisines) // On applique Navier-Stokes pour trouver l'accéleration
        {
                  
            double rhoi = 0;
            foreach (int i in Voisines)
            {
                rhoi += Kernel.KernelSimple((Systeme.Particules[i].Position - this.Position).Length);
            }
            this.MasseVolumique = rhoi;

            Vector Pression = new Vector(0, 0); // On accumule les forces de pression
            Vector Poids = new Vector(0, -MasseVolumique * 9.81);
            Vector Viscosite = new Vector(0,0); // et la viscosité
            foreach (int i in Voisines)
            {
                Vector Rayon = Systeme.Particules[i].Position - this.Position;
                Pression += Kernel.KernelGradient(constants.P0 * Systeme.Particules[i].MasseVolumique * Rayon);
                Viscosite += constants.CoeffViscosite * Kernel.KernelLaplacien(Rayon.Length) * Systeme.Particules[i].Vitesse;
            }
            Vector Forces = Pression + Poids + Viscosite;
            return Forces / MasseVolumique;
        } 

        public Tuple<Vector,Vector> CalculerPositionEtVitesse(Vector accelerations) // On intègre pour trouver la position
        {
            double VitesseX = (accelerations.X) * constants.pasTemporel + Vitesse.X;
            double VitesseY = (accelerations.Y) * constants.pasTemporel + Vitesse.Y;
            double positionX = VitesseX * constants.pasTemporel + Position.X;
            double positionY = VitesseY * constants.pasTemporel + Position.Y;
            return new Tuple<Vector, Vector>(new Vector(positionX, positionY), new Vector(VitesseX, VitesseY));
        }
    }






    static class Systeme
    {
        public static int NombreParticulesInstanciees = 0; // On compte le nombre d'instances 
        public static Particule[] Particules { get; set; } // Avantage: ce tableau sera long à initialiser mais ne bougera plus une fois initialisé
        public static Dictionary<Tuple<short, short>, List<int>> Grille { get; set; } // On repère les particules par leurs index
        public static int Etape = 0;
        //On empilera les différentes valeurs dans ces listes au fur et à mesure et modifiera les particules à la toute fin avec Update
        public static List<Vector> NouvellesPositions { get; set; }
        public static List<Vector> NouvellesVitesses { get; set; }
        public static List<Vector> NouvellesAccelerations { get; set; }


        public static void Initialisation(string cheminInput)
        {
            //Initialisation de la grille
            Grille = new Dictionary<Tuple<short, short>, List<int>>();
            for(short i=-50;i<=50;i++)
            {
                for(short j=-50;j<=50;j++)
                {
                    Grille.Add(new Tuple<short,short>(i,j),new List<int>());
                }
            }
            XDocument fichier = XDocument.Load(cheminInput); // Ouverture du fichier xml
            // On utilise LINQ to XML pour récupérer les conditions initiales dans le fichier
            IEnumerable<Particule> EtatInitial = from particule in fichier.Descendants("conditionsInitiales").Descendants("particule")
                                                      let position = new Vector((double)particule.Element("X"), (double)particule.Element("Y"))
                                                      let vitesse = new Vector((double)particule.Element("Vx"), (double)particule.Element("Vy"))
                                                      let acceleration = new Vector((double)particule.Element("Ax"), (double)particule.Element("Ay"))
                                                      select new Particule(position, vitesse, acceleration,(bool)particule.Element("mobile"));
            Systeme.Particules = EtatInitial.ToArray<Particule>();// Pour initialiser le système

            //Initialisation des constantes
            
            constants.masse = (double)fichier.Element("conditionsInitiales").Element("masse");
            constants.pasTemporel = (double)fichier.Element("conditionsInitiales").Element("pasTemporel");
            constants.rayonSPH = (double)fichier.Element("conditionsInitiales").Element("rayonSPH");
        }

        public static void Ecrire(string cheminOutput)
        {
            // Création de la nouvelle balise
            XElement Output = new XElement("Etape",
                                  new XAttribute("id", Etape++),
                                      from particule in Systeme.Particules
                                      select new XElement("Particule",
                                                 new XElement("X", particule.Position.X),
                                                 new XElement("Y", particule.Position.Y)));
            // Insertion de la nouvelle balise dans le fichier

            try
            {
                XDocument fichier = XDocument.Load(cheminOutput);
                fichier.Element("Output").Add(Output);
                fichier.Save(cheminOutput);
            }
            finally
            {
                Console.WriteLine("Erreur d'écriture à l'étape " + Etape.ToString()); 
            }
                               
        }

        public static void Experience(string cheminOutput, int nombreEtapes) 
        {
            Console.WriteLine("Début de l'experience, appuyez sur une touche pour continuer");
            Console.ReadKey(true);
            Ecrire(cheminOutput);
            for(int i=1;i<= nombreEtapes;i++)
            {
                Console.WriteLine("Etape " + i.ToString() + ": Début.");
                CalculerEtatSuivant();
                Ecrire(cheminOutput);
                Update();
                Console.WriteLine("Etape " + i.ToString() + ": Terminé.");
            }
            Console.WriteLine("Fin de l'experience.");
        }

        public static void CalculerEtatSuivant()
        {
            for (int i = 0; i < NombreParticulesInstanciees; i++)
            {
                List<int> Voisines = Particules[i].TrouverVoisines();
                Vector Acceleration = Particules[i].CalculerAcceleration(Voisines);
                Tuple<Vector, Vector> PositionEtVitesse = Particules[i].CalculerPositionEtVitesse(Acceleration);
                NouvellesPositions.Add(PositionEtVitesse.Item1);
                NouvellesVitesses.Add(PositionEtVitesse.Item2);
                NouvellesAccelerations.Add(Acceleration);
            }
        }

        public static void Update()
        {
            for (int i = 0; i < Particules.Length; i++)
            {
                Particules[i].Acceleration = NouvellesAccelerations[i];  // On a aucun soucis car on manipule des Vector créés dans une structure
                Particules[i].Vitesse = NouvellesVitesses[i];
                Particules[i].Position = NouvellesPositions[i];
                NouvellesAccelerations = new List<Vector>();
                NouvellesVitesses = new List<Vector>();
                NouvellesPositions = new List<Vector>();
            }
        }
    }

}