namespace Api.Cv;

// Where the master CV lives: config/cv.json unless Tracker:CvPath says otherwise (the tests point it at the example).
public sealed record CvSettings(string Path);
