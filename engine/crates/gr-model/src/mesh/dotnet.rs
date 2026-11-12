//! The two pieces of .NET behaviour that procedural geometry depends on bit for bit: the seeded random number
//! generator and the rounding of a few `System.Numerics.Vector3` operations.
//!
//! A plant is grown from random numbers seeded by its identifier. To grow the same plant as the application, the
//! same sequence is needed, so this is the generator `new Random(seed)` uses in .NET (Knuth's subtractive method,
//! kept unchanged for seeded instances), not a better one.

use glam::Vec3;

const BIG: i32 = i32::MAX;
const SEED: i32 = 161_803_398;

/// `System.Random` constructed with a seed.
pub struct DotNetRandom {
    state: [i32; 56],
    next: usize,
    next_p: usize,
}

impl DotNetRandom {
    pub fn new(seed: i32) -> DotNetRandom {
        let mut state = [0i32; 56];
        let subtraction = if seed == i32::MIN { i32::MAX } else { seed.abs() };
        let mut mj = SEED - subtraction;
        state[55] = mj;
        let mut mk = 1i32;
        let mut ii = 0usize;
        for _ in 1..55 {
            ii += 21;
            if ii >= 55 {
                ii -= 55;
            }
            state[ii] = mk;
            mk = mj - mk;
            if mk < 0 {
                mk += BIG;
            }
            mj = state[ii];
        }
        for _ in 1..5 {
            for i in 1..56 {
                let mut n = i + 30;
                if n >= 55 {
                    n -= 55;
                }
                state[i] = state[i].wrapping_sub(state[1 + n]);
                if state[i] < 0 {
                    state[i] += BIG;
                }
            }
        }
        DotNetRandom { state, next: 0, next_p: 21 }
    }

    fn sample(&mut self) -> i32 {
        self.next = if self.next + 1 >= 56 { 1 } else { self.next + 1 };
        self.next_p = if self.next_p + 1 >= 56 { 1 } else { self.next_p + 1 };
        let mut value = self.state[self.next].wrapping_sub(self.state[self.next_p]);
        if value == BIG {
            value -= 1;
        }
        if value < 0 {
            value += BIG;
        }
        self.state[self.next] = value;
        value
    }

    /// `Random.NextDouble()`: uniform in [0, 1).
    pub fn next_double(&mut self) -> f64 {
        self.sample() as f64 * (1.0 / BIG as f64)
    }

    /// `Random.Next(maxValue)`: an integer in [0, `max`).
    pub fn next_below(&mut self, max: i32) -> i32 {
        (self.next_double() * max as f64) as i32
    }

    /// `(float)random.NextDouble()`.
    pub fn next_f32(&mut self) -> f32 {
        self.next_double() as f32
    }

    /// A value in [-`amount`, `amount`), as `PlantGeometry.Jitter`.
    pub fn jitter(&mut self, amount: f32) -> f32 {
        (self.next_f32() * 2.0 - 1.0) * amount
    }
}

/// `Vector3.Normalize`: a division by the length, where `glam` multiplies by its reciprocal.
#[inline]
pub fn normalize(v: Vec3) -> Vec3 {
    v / v.length()
}

/// `Vector3.Lerp`: both ends weighted, where `glam` adds a scaled difference.
#[inline]
pub fn lerp(a: Vec3, b: Vec3, t: f32) -> Vec3 {
    a * (1.0 - t) + b * t
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_seeded_sequence_is_the_one_dotnet_produces() {
        // `new Random(42)`: the first values of Next(), documented and unchanged across .NET versions.
        let mut random = DotNetRandom::new(42);
        let first: Vec<i32> = (0..3).map(|_| random.sample()).collect();
        assert_eq!(first, [1_434_747_710, 302_596_119, 269_548_474]);
        // `new Random(0).Next()`.
        assert_eq!(DotNetRandom::new(0).sample(), 1_559_595_546);
    }

    #[test]
    fn doubles_stay_below_one_and_integers_below_their_bound() {
        let mut random = DotNetRandom::new(-7);
        for _ in 0..10_000 {
            let value = random.next_double();
            assert!((0.0..1.0).contains(&value));
            assert!((0..6).contains(&random.next_below(6)));
        }
    }
}
